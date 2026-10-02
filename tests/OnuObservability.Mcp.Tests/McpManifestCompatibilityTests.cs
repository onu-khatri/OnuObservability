using System.Text.Json;

namespace OnuObservability.Mcp.Tests;

public sealed class McpManifestCompatibilityTests
{
    [Fact]
    public void PackageSchemaMatchesCheckedAgentMemoryBaseline()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Baselines", "mcp-telemetry.json")));
        var root = document.RootElement;

        Assert.Equal(McpTelemetrySchema.Version, root.GetProperty("telemetrySchemaVersion").GetString());
        Assert.Equal(McpTelemetrySchema.ActivitySourceName, root.GetProperty("activitySource").GetString());
        Assert.Equal(McpTelemetrySchema.MeterName, root.GetProperty("meter").GetString());

        var expectedMetrics = root.GetProperty("metrics")
            .EnumerateArray()
            .Select(metric => new
            {
                Name = metric.GetProperty("name").GetString(),
                Type = metric.GetProperty("type").GetString(),
                Unit = metric.GetProperty("unit").GetString(),
                Dimensions = metric.GetProperty("dimensions").EnumerateArray()
                    .Select(value => value.GetString()).ToArray(),
                Histogram = metric.TryGetProperty("histogram", out var histogram)
                    ? histogram.GetString()
                    : null,
            }).ToArray();
        var actualMetrics = McpTelemetrySchema.Metrics.Select(metric => new
        {
            Name = (string?)metric.Name,
            Type = (string?)metric.Type,
            Unit = (string?)metric.Unit,
            Dimensions = metric.Dimensions.Select(value => (string?)value).ToArray(),
            Histogram = metric.Histogram,
        }).ToArray();
        Assert.Equal(
            JsonSerializer.Serialize(expectedMetrics),
            JsonSerializer.Serialize(actualMetrics));

        var expectedEvents = root.GetProperty("events").EnumerateArray()
            .Select(value => $"{value.GetProperty("id").GetInt32()}|{value.GetProperty("name").GetString()}")
            .ToArray();
        Assert.Equal(
            expectedEvents,
            McpTelemetrySchema.Events.Select(value => $"{value.Id}|{value.Name}"));

        var expectedActivities = root.GetProperty("activities").EnumerateArray()
            .Select(value => new
            {
                Name = value.TryGetProperty("name", out var name)
                    ? name.GetString()
                    : value.GetProperty("namePattern").GetString(),
                Kind = value.GetProperty("kind").GetString(),
                Attributes = value.GetProperty("attributes").EnumerateArray()
                    .Select(attribute => attribute.GetString()).ToArray(),
            }).ToArray();
        var actualActivities = McpTelemetrySchema.Activities.Select(value => new
        {
            Name = (string?)value.NamePattern,
            Kind = (string?)value.Kind,
            Attributes = value.Attributes.Select(attribute => (string?)attribute).ToArray(),
        }).ToArray();
        Assert.Equal(
            JsonSerializer.Serialize(expectedActivities),
            JsonSerializer.Serialize(actualActivities));
    }
}
