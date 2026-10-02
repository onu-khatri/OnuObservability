using System.Diagnostics;
using OnuObservability.Hosting.Telemetry;
using OnuObservability.Operations;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace OnuObservability.Hosting.Tests;

public sealed class ExporterResilienceTests
{
    [Fact]
    public void ComposedExportersReceiveTheSdkParentProviderOnNet8()
    {
        var health = new RecordingHealthObserver();
        var inner = new ParentCapturingExporter();
        using var guarded = new GuardedExporter<Activity>(
            inner,
            health,
            TelemetrySignal.Trace);
        using var provider = Sdk.CreateTracerProviderBuilder().Build();
        ExporterParentProvider.Assign(guarded, provider);
        using var activity = new Activity("test");

        Assert.Equal(ExportResult.Success, guarded.Export(new Batch<Activity>(activity)));
        Assert.NotNull(inner.ObservedParent);
        Assert.Empty(health.Records);
    }

    [Fact]
    public void GuardedExporterConvertsExporterExceptionsToBoundedFailure()
    {
        var health = new RecordingHealthObserver();
        using var exporter = new GuardedExporter<object>(
            new ThrowingExporter(),
            health,
            TelemetrySignal.Trace);
        var batch = new Batch<object>(new object());

        var result = exporter.Export(batch);

        Assert.Equal(ExportResult.Failure, result);
        Assert.Contains(health.Records, value =>
            value == (TelemetrySignal.Trace, ExportFailureReason.ExporterException, 1));
    }

    [Fact]
    public void GuardedExporterRecordsFailureAndFlushFailure()
    {
        var health = new RecordingHealthObserver();
        using var exporter = new GuardedExporter<object>(
            new FailingExporter(),
            health,
            TelemetrySignal.Log);
        var batch = new Batch<object>(new object());

        Assert.Equal(ExportResult.Failure, exporter.Export(batch));
        Assert.False(exporter.ForceFlush(10));

        Assert.Contains(health.Records, value =>
            value.Reason == ExportFailureReason.ExportFailure);
        Assert.Contains(health.Records, value =>
            value.Reason == ExportFailureReason.FlushFailure);
    }

    [Fact]
    public void PipelineDiagnosticsHaveFiniteKeysAndDoNotContainCallerData()
    {
        var writer = new StringWriter();
        var health = new TelemetryPipelineHealth(
            new OnuTelemetryInstruments(),
            TimeProvider.System,
            writer,
            TimeSpan.FromMinutes(1),
            burst: 1);

        for (var index = 0; index < 100; index++)
        {
            health.RecordDrop(
                (TelemetrySignal)(index % 3),
                (ExportFailureReason)(index % 5),
                index + 1);
        }

        Assert.True(health.KeyCount <= 15);
        Assert.True(health.DiagnosticCount <= 15);
        Assert.DoesNotContain("synthetic-secret", writer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("http", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TransportFailureRecordsOnlyBoundedSignalAndReason()
    {
        var health = new RecordingHealthObserver();
        using var invoker = new HttpMessageInvoker(new TelemetryHttpMessageHandler(
            health,
            TelemetrySignal.Metric,
            new ThrowingHandler()));
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://user:synthetic-secret@example.invalid/v1/metrics?token=private");

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            invoker.SendAsync(request, CancellationToken.None));

        Assert.Single(health.Records);
        Assert.Equal(TelemetrySignal.Metric, health.Records[0].Signal);
        Assert.Equal(ExportFailureReason.ExportFailure, health.Records[0].Reason);
    }

    [Fact]
    public void FullQueueDropsWithoutBlockingTheApplicationPath()
    {
        var health = new RecordingHealthObserver();
        using var inner = new BlockingExporter();
        using var processor = new BoundedBatchExportProcessor<object>(
            inner,
            health,
            TelemetrySignal.Trace,
            maximumQueueSize: 1,
            scheduledDelayMilliseconds: 1,
            exporterTimeoutMilliseconds: 1_000,
            maximumBatchSize: 1);

        processor.OnEnd(new object());
        Assert.True(inner.Started.Wait(TimeSpan.FromSeconds(2)));
        processor.OnEnd(new object());
        var started = System.Diagnostics.Stopwatch.StartNew();
        processor.OnEnd(new object());
        started.Stop();

        Assert.True(started.Elapsed < TimeSpan.FromMilliseconds(100));
        Assert.Contains(health.Records, value =>
            value.Reason == ExportFailureReason.QueueFull && value.Count == 1);

        inner.Release.Set();
        Assert.True(processor.ForceFlush(2_000));
    }

    [Fact]
    public void ExporterCanRecoverAfterARecordedFailure()
    {
        var health = new RecordingHealthObserver();
        using var exporter = new GuardedExporter<object>(
            new RecoveringExporter(),
            health,
            TelemetrySignal.Trace);
        var batch = new Batch<object>(new object());

        Assert.Equal(ExportResult.Failure, exporter.Export(batch));
        Assert.Equal(ExportResult.Success, exporter.Export(batch));

        Assert.Single(health.Records, value =>
            value.Reason == ExportFailureReason.ExportFailure);
    }

    private sealed class RecordingHealthObserver : IExportHealthObserver
    {
        public List<(TelemetrySignal Signal, ExportFailureReason Reason, long Count)> Records { get; } = [];

        public void RecordDrop(
            TelemetrySignal signal,
            ExportFailureReason reason,
            long count) => Records.Add((signal, reason, count));
    }

    private sealed class ThrowingExporter : BaseExporter<object>
    {
        public override ExportResult Export(in Batch<object> batch) =>
            throw new InvalidOperationException("synthetic-secret");
    }

    private sealed class FailingExporter : BaseExporter<object>
    {
        public override ExportResult Export(in Batch<object> batch) => ExportResult.Failure;

        protected override bool OnForceFlush(int timeoutMilliseconds) => false;
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("synthetic-secret");
    }

    private sealed class RecoveringExporter : BaseExporter<object>
    {
        private int _calls;

        public override ExportResult Export(in Batch<object> batch) =>
            Interlocked.Increment(ref _calls) == 1
                ? ExportResult.Failure
                : ExportResult.Success;
    }

    private sealed class ParentCapturingExporter : BaseExporter<Activity>
    {
        public BaseProvider? ObservedParent { get; private set; }

        public override ExportResult Export(in Batch<Activity> batch)
        {
            ObservedParent = ParentProvider;
            return ExportResult.Success;
        }

        protected override bool OnForceFlush(int timeoutMilliseconds) => true;
    }

    private sealed class BlockingExporter : BaseExporter<object>
    {
        public ManualResetEventSlim Started { get; } = new();

        public ManualResetEventSlim Release { get; } = new();

        public override ExportResult Export(in Batch<object> batch)
        {
            Started.Set();
            Release.Wait(TimeSpan.FromSeconds(5));
            return ExportResult.Success;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Started.Dispose();
                Release.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
