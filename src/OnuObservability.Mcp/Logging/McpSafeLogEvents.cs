using Microsoft.Extensions.Logging;

namespace OnuObservability.Mcp.Logging;

internal static partial class McpSafeLogEvents
{
    public const int ToolCompletedEventId = 1001;
    public const int ToolFailedEventId = 1002;
    public const int LogsSuppressedEventId = 1003;
    public const int DestructiveToolAttemptEventId = 1101;

    [LoggerMessage(
        EventId = ToolCompletedEventId,
        EventName = "mcp.tool.completed",
        Level = LogLevel.Information,
        Message = "MCP tool completed: tool={ToolName} outcome={Outcome} duration_seconds={DurationSeconds} input_items={InputItems} output_items={OutputItems}")]
    public static partial void ToolCompleted(
        ILogger logger,
        string toolName,
        string outcome,
        double durationSeconds,
        int inputItems,
        int outputItems);

    [LoggerMessage(
        EventId = ToolFailedEventId,
        EventName = "mcp.tool.failed",
        Level = LogLevel.Warning,
        Message = "MCP tool failed: tool={ToolName} outcome={Outcome} error_type={ErrorType} duration_seconds={DurationSeconds} input_items={InputItems} output_items={OutputItems}")]
    public static partial void ToolFailed(
        ILogger logger,
        string toolName,
        string outcome,
        string errorType,
        double durationSeconds,
        int inputItems,
        int outputItems);

    [LoggerMessage(
        EventId = LogsSuppressedEventId,
        EventName = "mcp.logs.suppressed",
        Level = LogLevel.Warning,
        Message = "MCP logs suppressed: event_name={SuppressedEventName} tool={ToolName} outcome={Outcome} count={Count}")]
    public static partial void LogsSuppressed(
        ILogger logger,
        string suppressedEventName,
        string toolName,
        string outcome,
        int count);

    [LoggerMessage(
        EventId = DestructiveToolAttemptEventId,
        EventName = "mcp.security.destructive_tool_attempt",
        Level = LogLevel.Warning,
        Message = "Destructive MCP tool attempted: tool={ToolName} policy_class=destructive outcome={Outcome}")]
    public static partial void DestructiveToolAttempt(
        ILogger logger,
        string toolName,
        string outcome);
}
