using OnuObservability.Mcp.Schema;

namespace OnuObservability.Mcp.Tests;

public sealed class McpTelemetrySchemaTests
{
    [Fact]
    public void MetricManifestPreservesEstablishedNamesTypesAndUnits()
    {
        var expected = new[]
        {
            "mcp.server.requests|counter<long>|{request}",
            "mcp.server.request.duration|histogram<double>|s",
            "mcp.server.inflight|updowncounter<long>|{request}",
            "mcp.tool.invocations|counter<long>|{invocation}",
            "mcp.tool.duration|histogram<double>|s",
            "mcp.tool.input.items|histogram<long>|{item}",
            "mcp.tool.output.items|histogram<long>|{item}",
            "mcp.tool.input.size|histogram<long>|By",
            "mcp.tool.output.size|histogram<long>|By",
            "mcp.validation.failures|counter<long>|{failure}",
            "mcp.authorization.denials|counter<long>|{denial}",
            "mcp.protocol.errors|counter<long>|{error}",
            "mcp.server.timeouts|counter<long>|{timeout}",
            "mcp.server.cancelled|counter<long>|{cancellation}",
            "mcp.dependency.errors|counter<long>|{error}",
            "mcp.logs.suppressed|counter<long>|{log}",
            "mcp.telemetry.dropped|counter<long>|{item}",
        };

        Assert.Equal(
            expected,
            McpTelemetrySchema.Metrics.Select(metric =>
                $"{metric.Name}|{metric.Type}|{metric.Unit}"));
    }

    [Theory]
    [InlineData("initialize", "initialize")]
    [InlineData(null, "unknown")]
    [InlineData("private-method", "other")]
    public void MethodNormalizationIsFinite(string? value, string expected) =>
        Assert.Equal(expected, McpTelemetrySchema.NormalizeMethod(value));

    [Theory]
    [InlineData("2025-11-25", "2025-11-25")]
    [InlineData(null, "unknown")]
    [InlineData("private-version", "other")]
    public void ProtocolNormalizationIsFinite(string? value, string expected) =>
        Assert.Equal(expected, McpTelemetrySchema.NormalizeProtocolVersion(value));
}
