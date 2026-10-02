using System.Collections.Immutable;
using OnuObservability.Model;
using OnuObservability.Safety;
using OnuObservability.Schema;

namespace OnuObservability.Operations;

public sealed record OperationResult
{
    public OperationResult(
        Outcome outcome,
        ErrorCode? error = null,
        ReasonCode? reason = null)
    {
        Outcome = outcome;
        Error = error;
        Reason = reason;
    }

    public Outcome Outcome { get; }
    public ErrorCode? Error { get; }
    public ReasonCode? Reason { get; }

    public static OperationResult Success { get; } = new(CommonOutcomes.Success);
}

public sealed record OperationStarted(
    OperationDescriptor Descriptor,
    OperationDescriptor? ParentDescriptor,
    DateTimeOffset StartedAtUtc);

public sealed record OperationCompleted(
    OperationDescriptor Descriptor,
    OperationDescriptor? ParentDescriptor,
    DateTimeOffset StartedAtUtc,
    TimeSpan Duration,
    OperationResult Result,
    ImmutableDictionary<string, TelemetryValue> Fields,
    ImmutableHashSet<string> TruncatedFields,
    ImmutableHashSet<string> ClampedFields,
    ImmutableHashSet<string> FallbackFields);

public interface IOperationSink
{
    IOperationObserver Start(OperationStarted operation);
}

public interface IOperationObserver
{
    void Complete(OperationCompleted operation);
}

public interface IOperationFactory
{
    IOperationScope Start(OperationDescriptor descriptor);
}

public interface IOperationScope : IDisposable
{
    OperationDescriptor Descriptor { get; }

    void Record(TelemetryFieldDescriptor field, TelemetryValue value);

    void AddCount(TelemetryFieldDescriptor field, long amount);

    void Complete(OperationResult result);

    bool TryComplete(OperationResult result);
}
