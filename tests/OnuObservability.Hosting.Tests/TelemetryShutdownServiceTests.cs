using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OnuObservability.Hosting.Composition;
using OnuObservability.Hosting.Configuration;
using OnuObservability.Hosting.Telemetry;
using OnuObservability.Operations;

namespace OnuObservability.Hosting.Tests;

public sealed class TelemetryShutdownServiceTests
{
    [Fact]
    public async Task HealthyFlushersCompleteWithinOneBudget()
    {
        var trace = new RecordingFlusher(TelemetrySignal.Trace);
        var metric = new RecordingFlusher(TelemetrySignal.Metric);
        var health = new RecordingHealthObserver();
        var service = new TelemetryShutdownService(
            [trace, metric],
            Settings(shutdownTimeoutMilliseconds: 500),
            health);

        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, trace.CallCount);
        Assert.Equal(1, metric.CallCount);
        Assert.Empty(health.Records);
        Assert.InRange(trace.LastTimeoutMilliseconds, 1, 500);
        Assert.InRange(metric.LastTimeoutMilliseconds, 1, trace.LastTimeoutMilliseconds);
    }

    [Fact]
    public async Task NonCooperativeFlusherCannotExtendShutdownBudget()
    {
        var health = new RecordingHealthObserver();
        var service = new TelemetryShutdownService(
            [new SleepingFlusher(TimeSpan.FromMilliseconds(500))],
            Settings(shutdownTimeoutMilliseconds: 30),
            health);
        var stopwatch = Stopwatch.StartNew();

        await service.StopAsync(CancellationToken.None);

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(250));
        Assert.Contains(health.Records, record =>
            record.Reason == ExportFailureReason.ShutdownFailure);
    }

    [Fact]
    public async Task SlowSignalCannotPreventHealthySignalFlush()
    {
        var trace = new SleepingFlusher(TimeSpan.FromMilliseconds(500));
        var metric = new RecordingFlusher(TelemetrySignal.Metric);
        var health = new RecordingHealthObserver();
        var service = new TelemetryShutdownService(
            [trace, metric],
            Settings(shutdownTimeoutMilliseconds: 50),
            health);

        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, metric.CallCount);
        Assert.InRange(metric.LastTimeoutMilliseconds, 1, 50);
    }

    [Fact]
    public void RegistrationDoesNotChangeGlobalHostShutdownTimeout()
    {
        var services = new ServiceCollection();
        services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(17));
        services.AddOnuObservability(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OnuObservability:ServiceName"] = "sample-service",
                ["OnuObservability:ShutdownFlushTimeoutMilliseconds"] = "50",
            })
            .Build());

        using var provider = services.BuildServiceProvider();

        Assert.Equal(
            TimeSpan.FromSeconds(17),
            provider.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout);
    }

    private static EffectiveObservabilitySettings Settings(int shutdownTimeoutMilliseconds)
    {
        var options = new OnuObservabilityOptions
        {
            ServiceName = "sample-service",
            ShutdownFlushTimeoutMilliseconds = shutdownTimeoutMilliseconds,
        };
        return ObservabilityConfiguration.CreateEffective(options);
    }

    private sealed class RecordingFlusher(TelemetrySignal signal) : IPackageTelemetryFlusher
    {
        public TelemetrySignal Signal => signal;

        public int CallCount { get; private set; }

        public int LastTimeoutMilliseconds { get; private set; }

        public bool Flush(int timeoutMilliseconds)
        {
            CallCount++;
            LastTimeoutMilliseconds = timeoutMilliseconds;
            return true;
        }
    }

    private sealed class SleepingFlusher(TimeSpan delay) : IPackageTelemetryFlusher
    {
        public TelemetrySignal Signal => TelemetrySignal.Trace;

        public bool Flush(int timeoutMilliseconds)
        {
            Thread.Sleep(delay);
            return true;
        }
    }

    private sealed class RecordingHealthObserver : IExportHealthObserver
    {
        public List<(TelemetrySignal Signal, ExportFailureReason Reason, long Count)> Records { get; } = [];

        public void RecordDrop(
            TelemetrySignal signal,
            ExportFailureReason reason,
            long count) => Records.Add((signal, reason, count));
    }
}
