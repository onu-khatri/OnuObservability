using System.Collections.Immutable;
using OnuObservability.Model;

namespace OnuObservability.Mcp.Schema;

public static class McpTelemetrySchema
{
    public const string ActivitySourceName = "AgentSession.MCP";
    public const string MeterName = "AgentSession.MCP";
    public const string SafeLogCategory = "AgentSession.MCP.Observability.Events";
    public const string SelfDiagnosticsCategory = "AgentSession.MCP.Observability.SelfDiagnostics";
    public const string Version = "1.0.0";
    public const string Unknown = "unknown";
    public const string Other = "other";
    public const string StdioTransport = "stdio";

    public static Outcome ClientCancelledOutcome { get; } = new("client_cancelled");
    public static Outcome ToolErrorOutcome { get; } = new("tool_error");
    public static Outcome ProtocolErrorOutcome { get; } = new("protocol_error");

    public static ImmutableArray<string> Methods { get; } =
    [
        "initialize",
        "server/discover",
        "ping",
        "tools/list",
        "tools/call",
        "logging/setLevel",
        Unknown,
        Other,
    ];

    public static ImmutableArray<string> ProtocolVersions { get; } =
        ["2025-11-25", "2026-07-28", Unknown, Other];

    public static ImmutableArray<double> LatencyHistogramBoundaries { get; } =
    [
        0.0005,
        0.001,
        0.0025,
        0.005,
        0.01,
        0.025,
        0.05,
        0.1,
        0.25,
        0.5,
        1,
        2.5,
        5,
        10,
        30,
        60,
    ];

    public static ImmutableArray<double> ItemHistogramBoundaries { get; } =
        [0, 1, 2, 4, 8, 16, 32, 64, 128, 256];

    public static ImmutableArray<double> SizeHistogramBoundaries { get; } =
        [64, 256, 1_024, 4_096, 16_384, 65_536, 262_144, 1_048_576];

    public static ImmutableArray<McpMetricDescriptor> Metrics { get; } =
    [
        new("mcp.server.requests", "counter<long>", "{request}", ["mcp.method.name", "mcp.status", "mcp.protocol.version", "mcp.transport"]),
        new("mcp.server.request.duration", "histogram<double>", "s", ["mcp.method.name", "mcp.status"], "latencySeconds"),
        new("mcp.server.inflight", "updowncounter<long>", "{request}", ["mcp.method.name"]),
        new("mcp.tool.invocations", "counter<long>", "{invocation}", ["mcp.tool.name", "mcp.status"]),
        new("mcp.tool.duration", "histogram<double>", "s", ["mcp.tool.name", "mcp.status"], "latencySeconds"),
        new("mcp.tool.input.items", "histogram<long>", "{item}", ["mcp.tool.name"], "itemCount"),
        new("mcp.tool.output.items", "histogram<long>", "{item}", ["mcp.tool.name"], "itemCount"),
        new("mcp.tool.input.size", "histogram<long>", "By", ["mcp.tool.name"], "byteSize"),
        new("mcp.tool.output.size", "histogram<long>", "By", ["mcp.tool.name"], "byteSize"),
        new("mcp.validation.failures", "counter<long>", "{failure}", ["mcp.tool.name", "reason"]),
        new("mcp.authorization.denials", "counter<long>", "{denial}", ["mcp.tool.name", "reason"]),
        new("mcp.protocol.errors", "counter<long>", "{error}", ["mcp.method.name", "reason"]),
        new("mcp.server.timeouts", "counter<long>", "{timeout}", ["mcp.method.name", "mcp.tool.name"]),
        new("mcp.server.cancelled", "counter<long>", "{cancellation}", ["mcp.method.name"]),
        new("mcp.dependency.errors", "counter<long>", "{error}", ["dependency.type", "dependency.operation"]),
        new("mcp.logs.suppressed", "counter<long>", "{log}", ["event.name", "mcp.tool.name", "mcp.status", "reason"]),
        new("mcp.telemetry.dropped", "counter<long>", "{item}", ["signal", "reason"]),
    ];

    public static ImmutableArray<McpActivityDescriptor> Activities { get; } =
    [
        new("mcp request", "server", ["mcp.method.name", "mcp.protocol.version", "mcp.transport", "mcp.status", "error.type"]),
        new("mcp tool {registered-tool-or-unknown}", "internal", ["mcp.tool.name", "mcp.transport", "mcp.status", "error.type"]),
        new("mcp dependency {dependency.type} {dependency.operation}", "client", ["dependency.type", "dependency.operation", "mcp.batch.items", "mcp.status", "error.type"]),
        new("mcp operation {dependency.type} {dependency.operation}", "internal", ["dependency.type", "dependency.operation", "mcp.batch.items", "mcp.status", "error.type", "mcp.work.deleted", "mcp.work.archived", "mcp.work.processed", "mcp.work.indexed", "mcp.work.deferred", "mcp.work.queued", "mcp.work.orphans", "mcp.work.errors", "mcp.recovery.performed"]),
    ];

    public static ImmutableArray<McpEventDescriptor> Events { get; } =
    [
        new(1001, "mcp.tool.completed"),
        new(1002, "mcp.tool.failed"),
        new(1003, "mcp.logs.suppressed"),
        new(1090, "mcp.telemetry.dropped"),
        new(1101, "mcp.security.destructive_tool_attempt"),
    ];

    public static string NormalizeMethod(string? value) =>
        Normalize(value, Methods);

    public static string NormalizeProtocolVersion(string? value) =>
        Normalize(value, ProtocolVersions);

    private static string Normalize(string? value, ImmutableArray<string> allowed) =>
        string.IsNullOrWhiteSpace(value)
            ? Unknown
            : allowed.Contains(value, StringComparer.Ordinal) ? value : Other;
}

public sealed record McpMetricDescriptor(
    string Name,
    string Type,
    string Unit,
    ImmutableArray<string> Dimensions,
    string? Histogram = null);

public sealed record McpActivityDescriptor(
    string NamePattern,
    string Kind,
    ImmutableArray<string> Attributes);

public sealed record McpEventDescriptor(int Id, string Name);
