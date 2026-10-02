using System.Collections.Concurrent;
using OnuObservability.Hosting.Background;
using OnuObservability.Model;
using OnuObservability.Operations;
using OnuObservability.Schema;

namespace OnuObservability.Hosting.Tests;

public sealed class BackgroundOperationRunnerTests
{
    private static readonly OperationDescriptor PassDescriptor =
        BackgroundOperationSchema.CreateDescriptor("worker.pass");

    [Fact]
    public async Task RunAsyncCreatesOneBoundaryAndRecordsOnlyAggregateCounts()
    {
        var sink = new RecordingSink();
        var runner = CreateRunner(sink);

        var result = await runner.RunAsync(
            PassDescriptor,
            default,
            _ => ValueTask.FromResult(new BackgroundOperationResult(
                new OperationResult(BackgroundOperationSchema.PartialOutcome),
                new BackgroundOperationCounts(100, 92, 3, 5, 2, 7))));

        Assert.Equal(BackgroundOperationSchema.PartialOutcome, result.Operation.Outcome);
        var completed = Assert.Single(sink.Completed);
        Assert.Equal("worker.pass", completed.Descriptor.Name);
        Assert.Equal(6, completed.Fields.Count);
        Assert.Equal(100, ReadCount(completed, BackgroundOperationSchema.Processed));
        Assert.Equal(5, ReadCount(completed, BackgroundOperationSchema.Deferred));
        Assert.Equal(2, ReadCount(completed, BackgroundOperationSchema.Retried));
    }

    [Theory]
    [InlineData(CancellationKind.Host, "host_stopped")]
    [InlineData(CancellationKind.Caller, "caller_cancelled")]
    [InlineData(CancellationKind.Deadline, "deadline_exceeded")]
    public async Task CancellationUsesTypedTokenContext(
        CancellationKind kind,
        string expectedOutcome)
    {
        using var host = new CancellationTokenSource();
        using var caller = new CancellationTokenSource();
        using var deadline = new CancellationTokenSource();
        TokenSource(kind, host, caller, deadline).Cancel();
        var sink = new RecordingSink();
        var runner = CreateRunner(sink);
        var cancellation = new BackgroundOperationCancellation(
            host.Token,
            caller.Token,
            deadline.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await runner.RunAsync(
                PassDescriptor,
                cancellation,
                _ => ValueTask.FromException<BackgroundOperationResult>(
                    new OperationCanceledException("caller-controlled-message"))));

        Assert.Equal(expectedOutcome, Assert.Single(sink.Completed).Result.Outcome.Value);
    }

    [Theory]
    [InlineData(BackgroundOperationFailureKind.Validation, "validation_error")]
    [InlineData(BackgroundOperationFailureKind.Dependency, "dependency_error")]
    [InlineData(BackgroundOperationFailureKind.Internal, "internal_error")]
    public async Task TypedFailuresAreClassifiedAndCompletedExactlyOnce(
        BackgroundOperationFailureKind kind,
        string expectedOutcome)
    {
        var sink = new RecordingSink();
        var runner = CreateRunner(sink);

        await Assert.ThrowsAsync<BackgroundOperationException>(async () =>
            await runner.RunAsync(
                PassDescriptor,
                default,
                _ => ValueTask.FromException<BackgroundOperationResult>(
                    new BackgroundOperationException(kind))));

        Assert.Equal(expectedOutcome, Assert.Single(sink.Completed).Result.Outcome.Value);
    }

    [Fact]
    public async Task DependencyOperationIsAChildOfTheCoarsePass()
    {
        var sink = new RecordingSink();
        var runner = CreateRunner(sink);
        var dependency = new OperationDescriptor("worker.dependency");

        await runner.RunAsync(
            PassDescriptor,
            default,
            async cancellation =>
            {
                await runner.RunDependencyAsync(
                    dependency,
                    default,
                    _ => ValueTask.FromResult(OperationResult.Success));
                return BackgroundOperationResult.Success(new BackgroundOperationCounts(1, 1));
            });

        var child = sink.Completed.Single(value => value.Descriptor == dependency);
        Assert.Equal(PassDescriptor, child.ParentDescriptor);
        Assert.Equal(2, sink.Completed.Count);
    }

    [Fact]
    public async Task InvalidCountsFailWithoutCreatingItemTelemetry()
    {
        var sink = new RecordingSink();
        var runner = CreateRunner(sink);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await runner.RunAsync(
                PassDescriptor,
                default,
                _ => ValueTask.FromResult(BackgroundOperationResult.Success(
                    new BackgroundOperationCounts(Processed: -1)))));

        var completed = Assert.Single(sink.Completed);
        Assert.Equal(CommonOutcomes.InternalError, completed.Result.Outcome);
        Assert.Empty(completed.Fields);
    }

    private static BackgroundOperationRunner CreateRunner(RecordingSink sink) =>
        new BackgroundOperationRunner(
            new OperationFactory(sink),
            new BackgroundOperationOutcomeClassifier());

    private static long ReadCount(
        OperationCompleted operation,
        TelemetryFieldDescriptor field)
    {
        Assert.True(operation.Fields[field.Name].TryGetInt64(out var value));
        return value;
    }

    private static CancellationTokenSource TokenSource(
        CancellationKind kind,
        CancellationTokenSource host,
        CancellationTokenSource caller,
        CancellationTokenSource deadline) =>
        kind switch
        {
            CancellationKind.Host => host,
            CancellationKind.Caller => caller,
            CancellationKind.Deadline => deadline,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    public enum CancellationKind
    {
        Host,
        Caller,
        Deadline,
    }

    private sealed class RecordingSink : IOperationSink
    {
        public ConcurrentBag<OperationCompleted> Completed { get; } = [];

        public IOperationObserver Start(OperationStarted operation) => new Observer(Completed);

        private sealed class Observer(ConcurrentBag<OperationCompleted> completed) : IOperationObserver
        {
            public void Complete(OperationCompleted operation) => completed.Add(operation);
        }
    }
}
