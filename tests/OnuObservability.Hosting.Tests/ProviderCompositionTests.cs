using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OnuObservability.Model;
using OnuObservability.Operations;
using OnuObservability.Schema;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace OnuObservability.Hosting.Tests;

public sealed class ProviderCompositionTests
{
    [Fact]
    public void PublicRegistrationEmitsTraceAndMetricSignals()
    {
        var activities = new List<Activity>();
        var metrics = new List<Metric>();
        var services = CreateServices(traceSamplingRatio: 1);
        services.ConfigureOpenTelemetryTracerProvider(tracing =>
            tracing.AddInMemoryExporter(activities));
        services.ConfigureOpenTelemetryMeterProvider(metering =>
            metering.AddInMemoryExporter(metrics));

        using var provider = services.BuildServiceProvider();
        var tracerProvider = provider.GetRequiredService<TracerProvider>();
        var meterProvider = provider.GetRequiredService<MeterProvider>();

        CompleteSampleOperation(provider);
        tracerProvider.ForceFlush();
        meterProvider.ForceFlush();

        var activity = Assert.Single(activities);
        Assert.Equal("sample.operation", activity.DisplayName);
        Assert.Contains(activity.TagObjects, tag =>
            tag.Key == "onu.outcome" && Equals(tag.Value, CommonOutcomes.Success.Value));
        Assert.Contains(metrics, metric => metric.Name == "onu.operation.count");
        Assert.Contains(metrics, metric => metric.Name == "onu.operation.duration");
    }

    [Fact]
    public void ZeroTraceSamplingStillEmitsMetrics()
    {
        var activities = new List<Activity>();
        var metrics = new List<Metric>();
        var services = CreateServices(traceSamplingRatio: 0);
        services.ConfigureOpenTelemetryTracerProvider(tracing =>
            tracing.AddInMemoryExporter(activities));
        services.ConfigureOpenTelemetryMeterProvider(metering =>
            metering.AddInMemoryExporter(metrics));

        using var provider = services.BuildServiceProvider();
        var tracerProvider = provider.GetRequiredService<TracerProvider>();
        var meterProvider = provider.GetRequiredService<MeterProvider>();

        CompleteSampleOperation(provider);
        tracerProvider.ForceFlush();
        meterProvider.ForceFlush();

        Assert.Empty(activities);
        Assert.Contains(metrics, metric => metric.Name == "onu.operation.count");
    }

    [Fact]
    public void DisabledConfigurationKeepsPublicOperationApiFunctional()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["OnuObservability:ServiceName"] = "sample-service",
            ["OnuObservability:Enabled"] = "false",
        });
        var services = new ServiceCollection();
        services.AddOnuObservability(configuration).AddOperation(new("sample.operation"));

        using var provider = services.BuildServiceProvider();

        CompleteSampleOperation(provider);
        Assert.Null(provider.GetService<TracerProvider>());
        Assert.Null(provider.GetService<MeterProvider>());
    }

    [Fact]
    public void ExportDisabledRequiresNoCollector()
    {
        var services = CreateServices(traceSamplingRatio: 1);
        using var provider = services.BuildServiceProvider();

        var error = Record.Exception(() =>
        {
            _ = provider.GetRequiredService<TracerProvider>();
            _ = provider.GetRequiredService<MeterProvider>();
            CompleteSampleOperation(provider);
        });

        Assert.Null(error);
    }

    private static ServiceCollection CreateServices(double traceSamplingRatio)
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["OnuObservability:ServiceName"] = "sample-service",
            ["OnuObservability:TraceSamplingRatio"] = traceSamplingRatio.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ["OnuObservability:ExporterEnabled"] = "false",
        });
        var services = new ServiceCollection();
        services.AddOnuObservability(configuration).AddOperation(new("sample.operation"));
        return services;
    }

    private static void CompleteSampleOperation(IServiceProvider provider)
    {
        var descriptor = provider.GetRequiredService<TelemetrySchemaSnapshot>()
            .Operations.Single(operation => operation.Name == "sample.operation");
        using var scope = provider.GetRequiredService<IOperationFactory>().Start(descriptor);
        scope.Complete(OperationResult.Success);
    }

    private static IConfiguration Configuration(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
