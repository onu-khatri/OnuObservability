using OnuObservability.Mcp.Schema;
using OnuObservability.Model;
using OnuObservability.Operations;

namespace OnuObservability.Mcp.Classification;

public enum McpValidationReason
{
    MissingRequired,
    WrongType,
    UnknownField,
    InvalidFormat,
    OutOfRange,
    DomainRule,
    Other,
}

public enum McpDenialReason
{
    ApprovalRequired,
    PolicyDenied,
    Other,
}

public sealed record McpTelemetryClassification(
    Outcome Outcome,
    ErrorCode? Error = null,
    McpValidationReason? ValidationReason = null,
    McpDenialReason? DenialReason = null)
{
    public OperationResult ToOperationResult() => new(Outcome, Error);
}

public interface IMcpTelemetryOutcomeClassifier
{
    McpTelemetryClassification Classify(
        Exception exception,
        CancellationToken callerCancellation = default);
}

public sealed class McpTelemetryOutcomeClassifier : IMcpTelemetryOutcomeClassifier
{
    public static McpTelemetryClassification Success { get; } =
        new(CommonOutcomes.Success);

    public McpTelemetryClassification Classify(
        Exception exception,
        CancellationToken callerCancellation = default)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            McpClassifiedException classified => classified.Classification,
            OperationCanceledException when callerCancellation.IsCancellationRequested =>
                new(McpTelemetrySchema.ClientCancelledOutcome, new("client_cancelled")),
            OperationCanceledException =>
                new(CommonOutcomes.DeadlineExceeded, new("deadline_exceeded")),
            TimeoutException =>
                new(CommonOutcomes.DeadlineExceeded, new("deadline_exceeded")),
            HttpRequestException =>
                new(CommonOutcomes.DependencyError, new("dependency_http_error")),
            InvalidDataException =>
                new(McpTelemetrySchema.ToolErrorOutcome, new("storage_invalid")),
            ModelContextProtocol.McpProtocolException =>
                new(McpTelemetrySchema.ProtocolErrorOutcome, new("protocol_error")),
            ModelContextProtocol.McpException =>
                new(McpTelemetrySchema.ToolErrorOutcome, new("mcp_error")),
            IOException =>
                new(McpTelemetrySchema.ToolErrorOutcome, new("storage_error")),
            _ => new(CommonOutcomes.InternalError, new("internal_error")),
        };
    }
}
