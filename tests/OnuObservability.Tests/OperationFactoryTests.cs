using System.Collections.Concurrent;
using OnuObservability.Model;
using OnuObservability.Operations;
using OnuObservability.Safety;
using OnuObservability.Schema;

namespace OnuObservability.Tests;

public sealed class OperationFactoryTests
{
    [Fact]
    public void CompletionIsExactlyOnceAndCarriesBoundedValues()
    {
        var sink = new RecordingSink();
        var factory = new OperationFactory(sink);
        var count = TelemetryFieldDescriptor.Count("processed", 10);
        var descriptor = new OperationDescriptor("sample.operation", [count]);
        using var scope = factory.Start(descriptor);
        scope.AddCount(count, 12);

        scope.Complete(OperationResult.Success);

        Assert.False(scope.TryComplete(new(CommonOutcomes.InternalError)));
        var completed = Assert.Single(sink.Completed);
        Assert.Equal("10", completed.Fields["processed"].ToInvariantString());
        Assert.Contains("processed", completed.ClampedFields);
        Assert.Equal(CommonOutcomes.Success, completed.Result.Outcome);
    }

    [Fact]
    public void DisposalCompletesAnAbandonedScopeAsInternalError()
    {
        var sink = new RecordingSink();
        var factory = new OperationFactory(sink);

        factory.Start(new("sample.operation")).Dispose();

        Assert.Equal(CommonOutcomes.InternalError, Assert.Single(sink.Completed).Result.Outcome);
    }

    [Fact]
    public void ExceptionUnwindStillCompletesTheScopeOnce()
    {
        var sink = new RecordingSink();
        var factory = new OperationFactory(sink);

        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using var scope = factory.Start(new("sample.operation"));
            throw new InvalidOperationException("synthetic failure");
        }));

        Assert.Equal(CommonOutcomes.InternalError, Assert.Single(sink.Completed).Result.Outcome);
    }

    [Fact]
    public void DurationUsesInjectedTimeProvider()
    {
        var sink = new RecordingSink();
        var time = new ManualTimeProvider();
        var factory = new OperationFactory(sink, time);
        using var scope = factory.Start(new("sample.operation"));

        time.Advance(TimeSpan.FromSeconds(3));
        scope.Complete(OperationResult.Success);

        Assert.Equal(TimeSpan.FromSeconds(3), Assert.Single(sink.Completed).Duration);
    }

    [Fact]
    public void ExplicitCancellationOutcomeIsPreserved()
    {
        var sink = new RecordingSink();
        var factory = new OperationFactory(sink);
        using var scope = factory.Start(new("sample.operation"));

        scope.Complete(new(CommonOutcomes.CallerCancelled, new("caller_cancelled")));

        Assert.Equal(CommonOutcomes.CallerCancelled, Assert.Single(sink.Completed).Result.Outcome);
    }

    [Fact]
    public void NestedScopesRestoreTheirParent()
    {
        var sink = new RecordingSink();
        var factory = new OperationFactory(sink);
        var parent = new OperationDescriptor("sample.parent");
        var child = new OperationDescriptor("sample.child");

        using var parentScope = factory.Start(parent);
        using (var childScope = factory.Start(child))
        {
            childScope.Complete(OperationResult.Success);
        }

        using (var secondChildScope = factory.Start(child))
        {
            secondChildScope.Complete(OperationResult.Success);
        }

        parentScope.Complete(OperationResult.Success);

        Assert.Equal(2, sink.Completed.Count(item => item.ParentDescriptor == parent));
        Assert.Null(sink.Completed.Single(item => item.Descriptor == parent).ParentDescriptor);
    }

    [Fact]
    public async Task ConcurrentChildScopesRemainIsolated()
    {
        var sink = new RecordingSink();
        var factory = new OperationFactory(sink);
        var parent = new OperationDescriptor("sample.parent");
        var child = new OperationDescriptor("sample.child");
        using var parentScope = factory.Start(parent);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
        {
            using var childScope = factory.Start(child);
            childScope.Complete(OperationResult.Success);
        })));
        parentScope.Complete(OperationResult.Success);

        Assert.Equal(20, sink.Completed.Count(item => item.ParentDescriptor == parent));
        Assert.Equal(21, sink.Completed.Length);
    }

    [Fact]
    public void UnregisteredFieldIsRejected()
    {
        var registered = TelemetryFieldDescriptor.Flag("registered");
        var unregistered = TelemetryFieldDescriptor.Flag("unregistered");
        using var scope = new OperationFactory(new RecordingSink())
            .Start(new("sample.operation", [registered]));

        Assert.Throws<ArgumentException>(() =>
            scope.Record(unregistered, TelemetryValue.FromFlag(true)));
    }

    private sealed class RecordingSink : IOperationSink
    {
        private readonly ConcurrentQueue<OperationCompleted> _completed = new();

        public OperationCompleted[] Completed => _completed.ToArray();

        public IOperationObserver Start(OperationStarted operation) => new Observer(_completed);

        private sealed class Observer(ConcurrentQueue<OperationCompleted> completed) : IOperationObserver
        {
            public void Complete(OperationCompleted operation) => completed.Enqueue(operation);
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.UnixEpoch;
        private long _timestamp;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override long GetTimestamp() => _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan duration)
        {
            _utcNow += duration;
            _timestamp += duration.Ticks;
        }
    }
}
