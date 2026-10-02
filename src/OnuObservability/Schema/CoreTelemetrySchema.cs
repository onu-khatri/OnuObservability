using OnuObservability.Logging;
using OnuObservability.Model;

namespace OnuObservability.Schema;

public static class CoreTelemetrySchema
{
    public const string Name = "onu.core";
    public const string Version = "1.0";
    public const string ExportOperationName = "onu.pipeline.export";
    public const string FlushOperationName = "onu.lifecycle.flush";

    public static TelemetrySchemaSnapshot Create()
    {
        var signal = TelemetryFieldDescriptor.FiniteString(
            "signal",
            ["trace", "metric", "log", "other"],
            "other",
            isRequired: true);
        var reason = TelemetryFieldDescriptor.FiniteString(
            "reason",
            ["rate_limit", "key_capacity", "export_failure", "flush_failure", "other"],
            "other");
        var count = TelemetryFieldDescriptor.Count("count", 1_000_000);

        var builder = new TelemetrySchemaBuilder(Name, SchemaVersion.Parse(Version))
            .AddOperation(new(ExportOperationName, [signal, count]))
            .AddOperation(new(FlushOperationName, [signal]))
            .AddEvent(new(
                OnuLogEvents.OperationCompletedEventId,
                "onu.operation.completed",
                "Operation completed"))
            .AddEvent(new(
                OnuLogEvents.OperationFailedEventId,
                "onu.operation.failed",
                "Operation failed"))
            .AddEvent(new(
                OnuLogEvents.SuppressionSummaryEventId,
                "onu.log.suppression_summary",
                "Suppressed events summary",
                [reason, count]));

        foreach (var outcome in CommonOutcomes.Registry.Values)
        {
            builder.AddOutcome(outcome);
        }

        foreach (var error in new[]
                 {
                     new ErrorCode("export_failure"),
                     new ErrorCode("flush_failure"),
                     new ErrorCode("other"),
                 })
        {
            builder.AddError(error);
        }

        foreach (var reasonCode in new[]
                 {
                     new ReasonCode("rate_limit"),
                     new ReasonCode("key_capacity"),
                     new ReasonCode("other"),
                 })
        {
            builder.AddReason(reasonCode);
        }

        return builder.Build();
    }
}
