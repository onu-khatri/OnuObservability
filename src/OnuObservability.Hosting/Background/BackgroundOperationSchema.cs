using OnuObservability.Model;
using OnuObservability.Schema;

namespace OnuObservability.Hosting.Background;

public static class BackgroundOperationSchema
{
    public const long MaximumCount = 1_000_000_000;

    public static TelemetryFieldDescriptor Processed { get; } =
        TelemetryFieldDescriptor.Count("work.processed", MaximumCount);

    public static TelemetryFieldDescriptor Succeeded { get; } =
        TelemetryFieldDescriptor.Count("work.succeeded", MaximumCount);

    public static TelemetryFieldDescriptor Failed { get; } =
        TelemetryFieldDescriptor.Count("work.failed", MaximumCount);

    public static TelemetryFieldDescriptor Deferred { get; } =
        TelemetryFieldDescriptor.Count("work.deferred", MaximumCount);

    public static TelemetryFieldDescriptor Retried { get; } =
        TelemetryFieldDescriptor.Count("work.retried", MaximumCount);

    public static TelemetryFieldDescriptor Remaining { get; } =
        TelemetryFieldDescriptor.Count("work.remaining", MaximumCount);

    public static Outcome PartialOutcome { get; } = new("partial");

    public static Outcome DeferredOutcome { get; } = new("deferred");

    public static ErrorCode ValidationError { get; } = new("validation_failure");

    public static ErrorCode DependencyError { get; } = new("dependency_failure");

    public static ErrorCode InternalError { get; } = new("internal_failure");

    public static ReasonCode HostStoppingReason { get; } = new("host_stopping");

    public static ReasonCode CallerCancellationReason { get; } = new("caller_cancellation");

    public static ReasonCode DeadlineReason { get; } = new("deadline");

    public static OperationDescriptor CreateDescriptor(
        string name,
        IEnumerable<TelemetryFieldDescriptor>? additionalFields = null)
    {
        var fields = new List<TelemetryFieldDescriptor>
        {
            Processed,
            Succeeded,
            Failed,
            Deferred,
            Retried,
            Remaining,
        };
        if (additionalFields is not null)
        {
            fields.AddRange(additionalFields);
        }

        return new OperationDescriptor(name, fields);
    }
}
