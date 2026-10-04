using Microsoft.Extensions.Logging;

namespace OnuObservability.Sample.Worker;

/// <summary>
/// Application-owned safe-log events. These are emitted through a category that is
/// whitelisted by a <c>SafeLogPolicy</c> registered at startup, so the local JSON
/// logger only serializes the explicitly approved fields (never exception text or
/// arbitrary payloads).
/// </summary>
internal static partial class WorkerEvents
{
    public const int AccountReconciledEventId = 2000;
    public const int AccountReconciliationFailedEventId = 2001;

    [LoggerMessage(
        EventId = AccountReconciledEventId,
        EventName = "worker.account.reconciled",
        Level = LogLevel.Information,
        Message = "Account batch reconciled: accounts={AccountCount} currency={Currency}")]
    public static partial void AccountReconciled(
        ILogger logger,
        int accountCount,
        string currency);

    [LoggerMessage(
        EventId = AccountReconciliationFailedEventId,
        EventName = "worker.account.reconciliation_failed",
        Level = LogLevel.Warning,
        Message = "Account batch reconciliation failed: outcome={Outcome} error={ErrorCode}")]
    public static partial void AccountReconciliationFailed(
        ILogger logger,
        string outcome,
        string errorCode);
}
