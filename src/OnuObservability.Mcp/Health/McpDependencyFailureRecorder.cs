using OnuObservability.Mcp.Telemetry;

namespace OnuObservability.Mcp.Health;

/// <summary>
/// Records a bounded MCP dependency failure without exposing OpenTelemetry SDK types.
/// </summary>
public interface IMcpDependencyFailureRecorder
{
    void Record(string dependencyType, string dependencyOperation);
}

internal sealed class McpDependencyFailureRecorder(
    McpTelemetryInstruments instruments) : IMcpDependencyFailureRecorder
{
    public void Record(string dependencyType, string dependencyOperation)
    {
        instruments.DependencyErrors.Add(
            1,
            new("dependency.type", McpTelemetryValues.NormalizeDependencyType(dependencyType)),
            new(
                "dependency.operation",
                McpTelemetryValues.NormalizeDependencyOperation(dependencyOperation)));
    }
}
