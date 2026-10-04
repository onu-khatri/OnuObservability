using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OnuObservability.Hosting.Composition;
using OnuObservability.Mcp.Filters;
using OnuObservability.Operations;

namespace OnuObservability.Mcp.Tests;

public sealed class McpStdioSafetyTests
{
    [Fact]
    public Task PackageLogsAndPipelineDiagnosticsUseJsonStderrOnly() => RunScenarioAsync();

    private static async Task RunScenarioAsync()
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OnuObservability:Enabled"] = "true",
                    ["OnuObservability:ExporterEnabled"] = "false",
                    ["OnuObservability:LocalJsonLoggingEnabled"] = "true",
                    ["OnuObservability:ServiceName"] = "stdio-test",
                })
                .Build();
            var services = new ServiceCollection();
            services.AddLogging();
            var observability = services.AddOnuObservability(configuration);
            var tool = McpServerTool.Create(
                (Func<string>)(() => "ok"),
                new McpServerToolCreateOptions { Name = "stdio_tool" });
            services
                .AddMcpServer()
                .WithTools([tool])
                .WithOnuObservability(observability);
            await using var provider = services.BuildServiceProvider();
            var initializer = provider.GetServices<IHostedService>()
                .Single(service => service is McpFilterRuntimeInitializer);
            await initializer.StartAsync(default);
            var filter = provider.GetRequiredService<McpToolTelemetryFilter>();

            await filter.InvokeCoreAsync(
                "stdio_tool",
                inputItems: 0,
                _ => ValueTask.FromResult(new CallToolResult { Content = [] }),
                default);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                filter.InvokeCoreAsync(
                    "stdio_tool",
                    inputItems: 0,
                    _ => ValueTask.FromException<CallToolResult>(
                        new InvalidOperationException("private-failure")),
                    default).AsTask());
            provider.GetRequiredService<IExportHealthObserver>().RecordDrop(
                TelemetrySignal.Trace,
                ExportFailureReason.ExportFailure,
                1);
            await initializer.StopAsync(default);

            Assert.Equal(string.Empty, stdout.ToString());
            var records = stderr.ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                Assert.Contains(records, record =>
                    record.RootElement.GetProperty("EventId").GetInt32() == 1001);
                Assert.Contains(records, record =>
                    record.RootElement.GetProperty("EventId").GetInt32() == 1002);
                Assert.Contains(records, record =>
                    record.RootElement.GetProperty("EventId").GetInt32() == 1090);
                Assert.DoesNotContain(
                    "private-failure",
                    stderr.ToString(),
                    StringComparison.Ordinal);
            }
            finally
            {
                foreach (var record in records)
                {
                    record.Dispose();
                }
            }
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
