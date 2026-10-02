using OnuObservability.Logging;
using OnuObservability.Model;
using OnuObservability.Schema;

namespace OnuObservability.Tests;

public sealed class BoundedEventRateLimiterTests
{
    [Fact]
    public void BurstAndWindowAreDeterministic()
    {
        var time = new ManualTimeProvider();
        var (schema, operation, eventDescriptor) = CreateSchema();
        var limiter = new BoundedEventRateLimiter(
            schema,
            time,
            TimeSpan.FromMinutes(1),
            burst: 2,
            maximumKeys: 8);

        Assert.True(limiter.Acquire(eventDescriptor, operation, CommonOutcomes.Success).IsAllowed);
        Assert.True(limiter.Acquire(eventDescriptor, operation, CommonOutcomes.Success).IsAllowed);
        Assert.False(limiter.Acquire(eventDescriptor, operation, CommonOutcomes.Success).IsAllowed);

        time.Advance(TimeSpan.FromMinutes(1));
        var renewed = limiter.Acquire(eventDescriptor, operation, CommonOutcomes.Success);

        Assert.True(renewed.IsAllowed);
        Assert.Equal(1, renewed.SuppressedToReport);
    }

    [Fact]
    public void CallerValuesCannotCreateKeys()
    {
        var time = new ManualTimeProvider();
        var (schema, operation, eventDescriptor) = CreateSchema();
        var limiter = new BoundedEventRateLimiter(
            schema,
            time,
            TimeSpan.FromMinutes(1),
            burst: 1,
            maximumKeys: 1);

        for (var index = 0; index < 10_000; index++)
        {
            limiter.Acquire(eventDescriptor, operation, new($"caller_unique_{index}"));
        }
        var unregistered = limiter.Acquire(
            new(99, "caller.event", "Caller event"),
            operation,
            CommonOutcomes.Success);

        Assert.Equal(1, limiter.KeyCount);
        Assert.False(unregistered.IsAllowed);
        Assert.Equal("unregistered_descriptor", unregistered.SuppressionReason);
    }

    [Fact]
    public async Task ConcurrentKeyAdmissionNeverExceedsCapacity()
    {
        var time = new ManualTimeProvider();
        var operations = Enumerable.Range(0, 64)
            .Select(index => new OperationDescriptor($"sample.operation_{index}"))
            .ToArray();
        var eventDescriptor = new EventDescriptor(10, "sample.completed", "Sample completed");
        var builder = new TelemetrySchemaBuilder("sample", new(1, 0))
            .AddEvent(eventDescriptor)
            .AddOutcome(CommonOutcomes.Success)
            .AddOutcome(CommonOutcomes.Other);
        foreach (var operation in operations)
        {
            builder.AddOperation(operation);
        }

        var limiter = new BoundedEventRateLimiter(
            builder.Build(),
            time,
            TimeSpan.FromMinutes(1),
            burst: 1,
            maximumKeys: 8);

        await Task.WhenAll(Enumerable.Range(0, 2_000).Select(index => Task.Run(() =>
            limiter.Acquire(
                eventDescriptor,
                operations[index % operations.Length],
                CommonOutcomes.Success))));

        Assert.InRange(limiter.KeyCount, 1, 8);
    }

    private static (TelemetrySchemaSnapshot Schema, OperationDescriptor Operation, EventDescriptor Event)
        CreateSchema()
    {
        var operation = new OperationDescriptor("sample.operation");
        var eventDescriptor = new EventDescriptor(10, "sample.completed", "Sample completed");
        var schema = new TelemetrySchemaBuilder("sample", new(1, 0))
            .AddOperation(operation)
            .AddEvent(eventDescriptor)
            .AddOutcome(CommonOutcomes.Success)
            .AddOutcome(CommonOutcomes.Other)
            .Build();
        return (schema, operation, eventDescriptor);
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
