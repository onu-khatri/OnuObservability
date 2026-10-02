using OnuObservability.Hosting.Configuration;
using OnuObservability.Hosting.Telemetry;
using OpenTelemetry.Resources;

namespace OnuObservability.Hosting.Tests;

public sealed class TelemetryResourceIdentityTests
{
    [Fact]
    public void ResourcesShareIdentityAndMetricsOmitProcessInstance()
    {
        var settings = ObservabilityConfiguration.CreateEffective(new()
        {
            ServiceName = "sample-service",
            ServiceNamespace = "sample-namespace",
            ServiceVersion = "2.3.4",
            DeploymentEnvironment = "test",
            TelemetrySchemaVersion = "1.2",
            PolicyVersion = "otel-stable-1",
        });
        var identity = new TelemetryResourceIdentity(settings);

        var trace = Attributes(identity.CreateTraceResource());
        var log = Attributes(identity.CreateLogResource());
        var metric = Attributes(identity.CreateMetricResource());

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

        Assert.Equal(identity.InstanceId, trace["service.instance.id"]);
        Assert.Equal(identity.InstanceId, log["service.instance.id"]);
        Assert.DoesNotContain("service.instance.id", metric.Keys);
    }

    private static Dictionary<string, object> Attributes(Resource resource) =>
        resource.Attributes.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}
