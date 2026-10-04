using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using OnuObservability.Hosting.Composition;
using OnuObservability.Mcp.Configuration;
using OnuObservability.Mcp.Filters;
using OnuObservability.Mcp.Health;
using OnuObservability.Mcp.Registry;
using OnuObservability.Mcp.Schema;

namespace OnuObservability.Mcp.Tests;

public sealed class McpRegistrationTests
{
    [Fact]
    public async Task DependencyFailureRecorderUsesAdapterInstrumentAndBoundedFallbacks()
    {
        var measurements = new List<(long Value, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == McpTelemetrySchema.MeterName &&
                    instrument.Name == "mcp.dependency.errors")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            measurements.Add((value, tags.ToArray().ToDictionary())));
        listener.Start();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OnuObservability:Enabled"] = "false",
                ["OnuObservability:ServiceName"] = "mcp-test",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        var observability = services.AddOnuObservability(configuration);
        services.AddMcpServer().WithOnuObservability(observability);
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IMcpDependencyFailureRecorder>()
            .Record("caller-secret", "private-operation");

        var measurement = Assert.Single(measurements);
        Assert.Equal(1, measurement.Value);
        Assert.Equal(McpTelemetrySchema.Other, measurement.Tags["dependency.type"]);
        Assert.Equal(McpTelemetrySchema.Other, measurement.Tags["dependency.operation"]);
    }

    [Fact]
    public async Task PublicRegistrationUsesSdkToolDescriptorsAndInitializesFilters()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OnuObservability:Enabled"] = "false",
                ["OnuObservability:ServiceName"] = "mcp-test",
                ["OnuObservability:Mcp:MaximumInputItems"] = "17",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        var observability = services.AddOnuObservability(configuration);
        var tool = McpServerTool.Create(
            (Func<string>)(() => "ok"),
            new McpServerToolCreateOptions { Name = "registered_tool" });
        services
            .AddMcpServer()
            .WithTools([tool])
            .WithOnuObservability(observability);
        await using var provider = services.BuildServiceProvider();

        var initializer = provider.GetServices<IHostedService>()
            .Single(service => service is McpFilterRuntimeInitializer);
        await initializer.StartAsync(default);

        var registry = provider.GetRequiredService<McpRegisteredToolRegistry>();
        Assert.Equal(["registered_tool"], registry.Names.ToArray());
        Assert.Equal("registered_tool", registry.Resolve(tool));
        Assert.Equal(17, provider.GetRequiredService<McpObservabilityOptions>().MaximumInputItems);
        Assert.NotNull(provider.GetRequiredService<McpRequestTelemetryFilter>());
        Assert.NotNull(provider.GetRequiredService<McpToolTelemetryFilter>());
    }
}
