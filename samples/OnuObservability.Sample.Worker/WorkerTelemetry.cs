using OnuObservability.Hosting.Background;
using OnuObservability.Hosting.Composition;
using OnuObservability.Hosting.Logging;
using OnuObservability.Model;
using OnuObservability.Schema;

namespace OnuObservability.Sample.Worker;

public static class WorkerTelemetry
{
    public const string SafeLogCategory = "Sample.Worker.Events";

    public static OperationDescriptor Pass { get; } =
        BackgroundOperationSchema.CreateDescriptor("sample.worker.pass");

    public static OperationDescriptor Dependency { get; } =
        new("sample.worker.dependency");

    // Application-owned domain outcomes/errors/reasons, registered exactly once.
    public static Outcome Reconciled { get; } = new("reconciled");
    public static ErrorCode AccountLocked { get; } = new("account_locked");
    public static ReasonCode MaintenanceWindow { get; } = new("maintenance_window");

    public static OnuObservabilityBuilder AddWorkerTelemetry(
        this OnuObservabilityBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddOperation(Pass)
            .AddOperation(Dependency)
            .AddOutcome(Reconciled)
            .AddError(AccountLocked)
            .AddReason(MaintenanceWindow)
            .AddEvent(new EventDescriptor(
                WorkerEvents.AccountReconciledEventId,
                "worker.account.reconciled",
                "Account batch reconciled"))
            .AddSafeLogPolicy(new SafeLogPolicy(
                SafeLogCategory,
                [
                    new SafeLogEventPolicy(
                        WorkerEvents.AccountReconciledEventId,
                        "worker.account.reconciled",
                        ["AccountCount", "Currency"]),
                    new SafeLogEventPolicy(
                        WorkerEvents.AccountReconciliationFailedEventId,
                        "worker.account.reconciliation_failed",
                        ["Outcome", "ErrorCode"]),
                ]));
    }
}
