using Microsoft.Extensions.Logging;

namespace OnuObservability.Logging;

public static partial class OnuLogEvents
{
    public const int OperationCompletedEventId = 1000;
    public const int OperationFailedEventId = 1001;
    public const int SuppressionSummaryEventId = 1002;

    [LoggerMessage(
        EventId = OperationCompletedEventId,
        Level = LogLevel.Information,
        Message = "Operation {OperationName} completed with outcome {Outcome} in {DurationMilliseconds} ms")]
    public static partial void OperationCompleted(
        ILogger logger,
        string operationName,
        string outcome,
        double durationMilliseconds);

    [LoggerMessage(
        EventId = OperationFailedEventId,
        Level = LogLevel.Warning,
        Message = "Operation {OperationName} failed with outcome {Outcome} and error {ErrorCode}")]
    public static partial void OperationFailed(
        ILogger logger,
        string operationName,
        string outcome,
        string errorCode);

    [LoggerMessage(
        EventId = SuppressionSummaryEventId,
        Level = LogLevel.Warning,
        Message = "Suppressed {SuppressedCount} events for {EventName}")]
    public static partial void SuppressionSummary(
        ILogger logger,
        int suppressedCount,
        string eventName);
}
