using System.Diagnostics;

namespace OnuObservability.Mcp;

internal static class McpTelemetrySources
{
    public static ActivitySource Activities { get; } =
        new(McpTelemetrySchema.ActivitySourceName, McpTelemetrySchema.Version);
}
