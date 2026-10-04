using System.Diagnostics;
using OnuObservability.Mcp.Schema;

namespace OnuObservability.Mcp.Telemetry;

internal static class McpTelemetrySources
{
    public static ActivitySource Activities { get; } =
        new(McpTelemetrySchema.ActivitySourceName, McpTelemetrySchema.Version);
}
