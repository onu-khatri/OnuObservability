using System.Reflection;
using OnuObservability.Hosting.Configuration;
using OpenTelemetry.Resources;

namespace OnuObservability.Hosting.Telemetry;

internal sealed class TelemetryResourceIdentity
{
    private readonly EffectiveObservabilitySettings _settings;

    public TelemetryResourceIdentity(EffectiveObservabilitySettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        InstanceId = Guid.NewGuid().ToString("N");
        ServiceVersion = settings.ServiceVersion ?? ResolveServiceVersion();
    }

    public string InstanceId { get; }

    public string ServiceVersion { get; }

    public Resource CreateTraceResource() => Create(includeInstance: true);

    public Resource CreateLogResource() => Create(includeInstance: true);

    public Resource CreateMetricResource() => Create(includeInstance: false);

    private Resource Create(bool includeInstance)
    {
        var builder = ResourceBuilder.CreateEmpty()
            .AddService(
                _settings.ServiceName,
                _settings.ServiceNamespace,
                ServiceVersion,
                autoGenerateServiceInstanceId: false,
                serviceInstanceId: includeInstance ? InstanceId : null)
            .AddAttributes(
            [
                new("deployment.environment.name", _settings.DeploymentEnvironment),
                new("process.runtime.name", ".NET"),
                new("process.runtime.version", Environment.Version.ToString()),
                new("telemetry.schema.version", _settings.TelemetrySchemaVersion),
                new("telemetry.policy.version", _settings.PolicyVersion),
            ]);
        return builder.Build();
    }

    private static string ResolveServiceVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(TelemetryResourceIdentity).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                   ?.InformationalVersion.Split('+')[0] ??
               assembly.GetName().Version?.ToString() ??
               "unknown";
    }
}
