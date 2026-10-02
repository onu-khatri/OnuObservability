using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace OnuObservability.Mcp;

public sealed class McpRegisteredToolRegistry
{
    private readonly ImmutableDictionary<string, McpServerTool> _tools;

    public McpRegisteredToolRegistry(IEnumerable<McpServerTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var materialized = tools.OrderBy(
            tool => tool.ProtocolTool.Name,
            StringComparer.Ordinal).ToArray();
        if (materialized.Select(tool => tool.ProtocolTool.Name)
            .Distinct(StringComparer.Ordinal).Count() != materialized.Length)
        {
            throw new ArgumentException("Registered MCP tool names must be unique.", nameof(tools));
        }

        _tools = materialized.ToImmutableDictionary(
            tool => tool.ProtocolTool.Name,
            StringComparer.Ordinal);
        Names = _tools.Keys.Order(StringComparer.Ordinal).ToImmutableArray();
        ToolsetVersion = ComputeVersion(materialized);
    }

    public ImmutableArray<string> Names { get; }

    public string ToolsetVersion { get; }

    public string Resolve(IMcpServerPrimitive? primitive)
    {
        var candidate = (primitive as McpServerTool)?.ProtocolTool.Name;
        return candidate is not null && _tools.TryGetValue(candidate, out var registered) &&
               ReferenceEquals(primitive, registered)
            ? candidate
            : McpTelemetrySchema.Unknown;
    }

    public bool IsDestructive(IMcpServerPrimitive? primitive) =>
        primitive is McpServerTool tool &&
        _tools.TryGetValue(tool.ProtocolTool.Name, out var registered) &&
        ReferenceEquals(tool, registered) &&
        registered.ProtocolTool.Annotations?.DestructiveHint is true;

    private static string ComputeVersion(IEnumerable<McpServerTool> tools)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var tool in tools)
            {
                writer.WriteStartObject();
                writer.WriteString("name", tool.ProtocolTool.Name);
                writer.WriteBoolean(
                    "destructive",
                    tool.ProtocolTool.Annotations?.DestructiveHint is true);
                writer.WritePropertyName("input");
                WriteCanonical(writer, tool.ProtocolTool.InputSchema);
                writer.WritePropertyName("output");
                if (tool.ProtocolTool.OutputSchema is { } output)
                {
                    WriteCanonical(writer, output);
                }
                else
                {
                    writer.WriteNullValue();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return "sha256:" + Convert.ToHexString(SHA256.HashData(stream.ToArray()))[..16]
            .ToLowerInvariant();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject()
                    .OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
