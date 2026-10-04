using ModelContextProtocol;

namespace OnuObservability.Mcp.Classification;

public sealed class McpClassifiedException : McpException
{
    public McpClassifiedException(
        string safeClientMessage,
        McpTelemetryClassification classification)
        : base(safeClientMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(safeClientMessage);
        Classification = classification ?? throw new ArgumentNullException(nameof(classification));
    }

    public McpTelemetryClassification Classification { get; }

    public static McpClassifiedException Create(
        string safeClientMessage,
        McpTelemetryClassification classification,
        IMcpTelemetryOutcomeContext outcomeContext)
    {
        ArgumentNullException.ThrowIfNull(outcomeContext);
        outcomeContext.Record(classification);
        return new(safeClientMessage, classification);
    }
}
