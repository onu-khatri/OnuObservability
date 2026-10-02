using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OnuObservability.Operations;
using OnuObservability.Schema;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace OnuObservability.Hosting.Tests;

public sealed class ResourceExporterIntegrationTests
{
    [Fact]
    public void TraceLogAndMetricExportersReceiveConsistentResources()
    {
        var traceExporter = new ResourceCapturingExporter<Activity>();
        var logExporter = new ResourceCapturingExporter<LogRecord>();
        var metricExporter = new ResourceCapturingExporter<Metric>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OnuObservability:ServiceName"] = "sample-service",
                ["OnuObservability:ServiceNamespace"] = "sample-namespace",
                ["OnuObservability:ServiceVersion"] = "2.3.4",
                ["OnuObservability:DeploymentEnvironment"] = "test",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddOnuObservability(configuration).AddOperation(new("sample.operation"));
        services.ConfigureOpenTelemetryTracerProvider(tracing =>
            tracing.AddProcessor(new SimpleActivityExportProcessor(traceExporter)));
        services.ConfigureOpenTelemetryMeterProvider(metrics =>
            metrics.AddReader(new PeriodicExportingMetricReader(
                metricExporter,
                exportIntervalMilliseconds: 60_000)));
        services.AddLogging(logging => logging.AddOpenTelemetry(options =>
            options.AddProcessor(new SimpleLogRecordExportProcessor(logExporter))));

        using var provider = services.BuildServiceProvider();
        var tracerProvider = provider.GetRequiredService<TracerProvider>();
        var meterProvider = provider.GetRequiredService<MeterProvider>();
        var descriptor = provider.GetRequiredService<TelemetrySchemaSnapshot>()
            .Operations.Single(operation => operation.Name == "sample.operation");
        using (var scope = provider.GetRequiredService<IOperationFactory>().Start(descriptor))
        {
            scope.Complete(OperationResult.Success);
        }

        tracerProvider.ForceFlush();
        meterProvider.ForceFlush();

        var trace = Attributes(Assert.IsType<Resource>(traceExporter.Resource));
        var log = Attributes(Assert.IsType<Resource>(logExporter.Resource));
        var metric = Attributes(Assert.IsType<Resource>(metricExporter.Resource));
        foreach (var key in new[]
                 {
                     "service.name",
                     "service.namespace",
                     "service.version",
                     "deployment.environment.name",
                     "process.runtime.name",
                     "process.runtime.version",
                     "telemetry.schema.version",
                     "telemetry.policy.version",
                 })
        {
            Assert.Equal(trace[key], log[key]);
            Assert.Equal(trace[key], metric[key]);
        }

        Assert.Equal(trace["service.instance.id"], log["service.instance.id"]);
        Assert.DoesNotContain("service.instance.id", metric.Keys);
    }

    private static Dictionary<string, object> Attributes(Resource resource) =>
        resource.Attributes.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private sealed class ResourceCapturingExporter<T> : BaseExporter<T>
        where T : class
    {
        public Resource? Resource { get; private set; }

        public override ExportResult Export(in Batch<T> batch)
        {
            Resource = ParentProvider?.GetResource();
            return ExportResult.Success;
        }
    }
}
