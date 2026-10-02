using System.Collections.Frozen;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace OnuObservability.Hosting.Configuration;

public static class ObservabilityConfiguration
{
    public static IServiceCollection AddOnuObservabilityConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<OnuObservabilityOptions>()
            .Bind(configuration.GetSection(OnuObservabilityOptions.SectionName))
            .Configure(options => ApplyOpenTelemetryEnvironment(configuration, options))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<OnuObservabilityOptions>,
            ObservabilityOptionsValidator>());
        services.TryAddSingleton(CreateEffective(BindEffectiveOptions(configuration)));
        return services;
    }

    public static OnuObservabilityOptions BindEffectiveOptions(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var options = new OnuObservabilityOptions();
        configuration.GetSection(OnuObservabilityOptions.SectionName).Bind(options);
        ApplyOpenTelemetryEnvironment(configuration, options);
        return options;
    }

    public static EffectiveObservabilitySettings CreateEffective(OnuObservabilityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var validation = new ObservabilityOptionsValidator().Validate(null, options);
        if (validation.Failed)
        {
            throw new OptionsValidationException(
                OnuObservabilityOptions.SectionName,
                typeof(OnuObservabilityOptions),
                validation.Failures);
        }

        _ = ObservabilityOptionsValidator.TryParseProtocol(options.ExporterProtocol, out var protocol);
        return new(
            options.Enabled,
            options.ExporterEnabled,
            options.ExportTraces,
            options.ExportMetrics,
            options.ExportLogs,
            options.LocalJsonLoggingEnabled,
            options.ServiceName,
            options.ServiceNamespace,
            options.ServiceVersion,
            options.DeploymentEnvironment,
            options.TelemetrySchemaVersion,
            options.PolicyVersion,
            protocol,
            options.Endpoint is null ? null : new Uri(options.Endpoint, UriKind.Absolute),
            (options.InsecureSidecarHosts ?? [])
                .ToFrozenSet(StringComparer.OrdinalIgnoreCase),
            options.TraceSamplingRatio,
            TimeSpan.FromMilliseconds(options.ExportTimeoutMilliseconds),
            TimeSpan.FromMilliseconds(options.BatchDelayMilliseconds),
            options.QueueCapacity,
            options.MaximumExportBatchSize,
            TimeSpan.FromMilliseconds(options.ShutdownFlushTimeoutMilliseconds),
            options.MaximumLogBodyLength,
            options.MaximumAttributeLength,
            TimeSpan.FromSeconds(options.LogRateLimitWindowSeconds),
            options.LogRateLimitBurst,
            options.MaximumLogRateLimitKeys);
    }

    private static void ApplyOpenTelemetryEnvironment(
        IConfiguration configuration,
        OnuObservabilityOptions options)
    {
        if (bool.TryParse(configuration["OTEL_SDK_DISABLED"], out var disabled) && disabled)
        {
            options.Enabled = false;
        }

        Override(configuration, "OTEL_SERVICE_NAME", value => options.ServiceName = value);
        Override(configuration, "OTEL_EXPORTER_OTLP_ENDPOINT", value =>
        {
            options.Endpoint = value;
            options.ExporterEnabled = true;
        });
        Override(configuration, "OTEL_EXPORTER_OTLP_PROTOCOL", value =>
            options.ExporterProtocol = value.ToLowerInvariant());

        if (double.TryParse(
                configuration["OTEL_TRACES_SAMPLER_ARG"],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var ratio))
        {
            options.TraceSamplingRatio = ratio;
        }

        ApplySignalExporter(configuration["OTEL_TRACES_EXPORTER"], value => options.ExportTraces = value);
        ApplySignalExporter(configuration["OTEL_METRICS_EXPORTER"], value => options.ExportMetrics = value);
        ApplySignalExporter(configuration["OTEL_LOGS_EXPORTER"], value => options.ExportLogs = value);

        var resources = configuration["OTEL_RESOURCE_ATTRIBUTES"];
        if (string.IsNullOrWhiteSpace(resources))
        {
            return;
        }

        foreach (var entry in resources.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = entry.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = entry[..separator].Trim();
            var value = entry[(separator + 1)..].Trim();
            if (key is "deployment.environment" or "deployment.environment.name")
            {
                options.DeploymentEnvironment = value;
            }
            else if (key == "service.namespace")
            {
                options.ServiceNamespace = value;
            }
        }
    }

    private static void ApplySignalExporter(string? value, Action<bool> apply)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var exporters = value.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        apply(exporters.Contains("otlp", StringComparer.OrdinalIgnoreCase));
    }

    private static void Override(
        IConfiguration configuration,
        string key,
        Action<string> apply)
    {
        if (configuration[key] is { Length: > 0 } value)
        {
            apply(value);
        }
    }
}
