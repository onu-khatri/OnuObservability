using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OnuObservability.Hosting.Logging;
using OnuObservability.Hosting.Telemetry;
using OnuObservability.Model;
using OnuObservability.Operations;
using OnuObservability.Schema;

namespace OnuObservability.Hosting;

public sealed class OnuObservabilityBuilder
{
    private readonly TelemetrySchemaBuilder _schema;
    private readonly SafeLogPolicyRegistry _safeLogs;
    private readonly ExportHealthObserverRegistry _exportHealth;

    internal OnuObservabilityBuilder(
        IServiceCollection services,
        TelemetrySchemaBuilder schema,
        SafeLogPolicyRegistry safeLogs,
        ExportHealthObserverRegistry exportHealth,
        IConfiguration configuration)
    {
        Services = services;
        _schema = schema;
        _safeLogs = safeLogs;
        _exportHealth = exportHealth;
        Configuration = configuration;
    }

    public IServiceCollection Services { get; }

    public IConfiguration Configuration { get; }

    public OnuObservabilityBuilder AddOperation(OperationDescriptor descriptor)
    {
        _schema.AddOperation(descriptor);
        return this;
    }

    public OnuObservabilityBuilder AddEvent(EventDescriptor descriptor)
    {
        _schema.AddEvent(descriptor);
        return this;
    }

    public OnuObservabilityBuilder AddOutcome(Outcome outcome)
    {
        _schema.AddOutcome(outcome);
        return this;
    }

    public OnuObservabilityBuilder AddError(ErrorCode error)
    {
        _schema.AddError(error);
        return this;
    }

    public OnuObservabilityBuilder AddReason(ReasonCode reason)
    {
        _schema.AddReason(reason);
        return this;
    }

    public OnuObservabilityBuilder AddSafeLogPolicy(SafeLogPolicy policy)
    {
        _safeLogs.Add(policy);
        return this;
    }

    public OnuObservabilityBuilder AddExportHealthObserver(IExportHealthObserver observer)
    {
        _exportHealth.Add(observer);
        return this;
    }

    internal TelemetrySchemaSnapshot BuildSchema() => _schema.Build();
}
