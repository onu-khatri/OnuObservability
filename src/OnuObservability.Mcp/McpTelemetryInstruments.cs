using System.Diagnostics.Metrics;

namespace OnuObservability.Mcp;

public sealed class McpTelemetryInstruments : IDisposable
{
    private readonly Meter _meter = new(McpTelemetrySchema.MeterName);

    public McpTelemetryInstruments()
    {
        ServerRequests = _meter.CreateCounter<long>("mcp.server.requests", "{request}");
        ServerRequestDuration = _meter.CreateHistogram<double>("mcp.server.request.duration", "s");
        ServerInflight = _meter.CreateUpDownCounter<long>("mcp.server.inflight", "{request}");
        ToolInvocations = _meter.CreateCounter<long>("mcp.tool.invocations", "{invocation}");
        ToolDuration = _meter.CreateHistogram<double>("mcp.tool.duration", "s");
        ToolInputItems = _meter.CreateHistogram<long>("mcp.tool.input.items", "{item}");
        ToolOutputItems = _meter.CreateHistogram<long>("mcp.tool.output.items", "{item}");
        ToolInputSize = _meter.CreateHistogram<long>("mcp.tool.input.size", "By");
        ToolOutputSize = _meter.CreateHistogram<long>("mcp.tool.output.size", "By");
        ValidationFailures = _meter.CreateCounter<long>("mcp.validation.failures", "{failure}");
        AuthorizationDenials = _meter.CreateCounter<long>("mcp.authorization.denials", "{denial}");
        ProtocolErrors = _meter.CreateCounter<long>("mcp.protocol.errors", "{error}");
        ServerTimeouts = _meter.CreateCounter<long>("mcp.server.timeouts", "{timeout}");
        ServerCancelled = _meter.CreateCounter<long>("mcp.server.cancelled", "{cancellation}");
        DependencyErrors = _meter.CreateCounter<long>("mcp.dependency.errors", "{error}");
        LogsSuppressed = _meter.CreateCounter<long>("mcp.logs.suppressed", "{log}");
        TelemetryDropped = _meter.CreateCounter<long>("mcp.telemetry.dropped", "{item}");
    }

    internal Counter<long> ServerRequests { get; }
    internal Histogram<double> ServerRequestDuration { get; }
    internal UpDownCounter<long> ServerInflight { get; }
    internal Counter<long> ToolInvocations { get; }
    internal Histogram<double> ToolDuration { get; }
    internal Histogram<long> ToolInputItems { get; }
    internal Histogram<long> ToolOutputItems { get; }
    internal Histogram<long> ToolInputSize { get; }
    internal Histogram<long> ToolOutputSize { get; }
    internal Counter<long> ValidationFailures { get; }
    internal Counter<long> AuthorizationDenials { get; }
    internal Counter<long> ProtocolErrors { get; }
    internal Counter<long> ServerTimeouts { get; }
    internal Counter<long> ServerCancelled { get; }
    internal Counter<long> DependencyErrors { get; }
    internal Counter<long> LogsSuppressed { get; }
    internal Counter<long> TelemetryDropped { get; }

    public void Dispose() => _meter.Dispose();
}
