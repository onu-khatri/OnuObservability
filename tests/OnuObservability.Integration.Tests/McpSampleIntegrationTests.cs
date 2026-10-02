using System.Text.Json;

namespace OnuObservability.Integration.Tests;

public sealed class McpSampleIntegrationTests
{
    [Fact]
    public async Task SamplePreservesMcpContractsAndKeepsTelemetryOffStdout()
    {
        const string sensitiveValue = SensitiveDataCorpus.Canary;
        await using var process = new McpSampleProcess(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = Environment.GetEnvironmentVariable(
                "ONU_TEST_OTLP_ENDPOINT") ?? "http://127.0.0.1:1",
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            ["OTEL_TRACES_EXPORTER"] = "otlp",
            ["OTEL_METRICS_EXPORTER"] = "otlp",
            ["OTEL_LOGS_EXPORTER"] = "otlp",
            ["OnuObservability__BatchDelayMilliseconds"] = "50",
            ["OnuObservability__ExportTimeoutMilliseconds"] = "100",
            ["OnuObservability__ShutdownFlushTimeoutMilliseconds"] = "1000",
            ["OnuObservability__QueueCapacity"] = "16",
            ["OnuObservability__MaximumExportBatchSize"] = "8",
            ["OTEL_RESOURCE_ATTRIBUTES"] = $"private.token={SensitiveDataCorpus.Canary}",
        });

        await process.InitializeAsync();

        var listed = await process.RequestAsync("tools/list", new { });
        Assert.False(listed.TryGetProperty("error", out _), listed.ToString());
        var tools = listed.GetProperty("result").GetProperty("tools");
        Assert.Contains(tools.EnumerateArray(), tool => tool.GetProperty("name").GetString() == "echo");
        Assert.Contains(tools.EnumerateArray(), tool => tool.GetProperty("name").GetString() == "fail");

        var echoed = await process.RequestAsync(
            "tools/call",
            new { name = "echo", arguments = new { value = sensitiveValue } });
        Assert.False(echoed.TryGetProperty("error", out _), echoed.ToString());
        var echoResult = echoed.GetProperty("result");
        Assert.False(echoResult.TryGetProperty("isError", out var echoError) && echoError.GetBoolean());
        Assert.Contains(
            sensitiveValue,
            echoResult.GetProperty("content").EnumerateArray()
                .Select(content => content.GetProperty("text").GetString()));

        var failed = await process.RequestAsync(
            "tools/call",
            new { name = "fail", arguments = new { } });
        Assert.False(failed.TryGetProperty("error", out _), failed.ToString());
        Assert.True(failed.GetProperty("result").GetProperty("isError").GetBoolean());

        var stderr = await process.StopAsync();
        Assert.DoesNotContain(sensitiveValue, stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("sample failure", stderr, StringComparison.OrdinalIgnoreCase);
        Assert.All(
            stderr.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries),
            line =>
            {
                using var record = JsonDocument.Parse(line);
                Assert.Equal(JsonValueKind.Object, record.RootElement.ValueKind);
            });
    }
}
