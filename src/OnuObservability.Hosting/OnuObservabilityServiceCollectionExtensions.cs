using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OnuObservability.Hosting.Background;
using OnuObservability.Hosting.Configuration;
using OnuObservability.Hosting.Logging;
using OnuObservability.Hosting.Telemetry;
using OnuObservability.Logging;
using OnuObservability.Operations;
using OnuObservability.Schema;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace OnuObservability.Hosting;

public static class OnuObservabilityServiceCollectionExtensions
{
    private static readonly double[] DurationBoundaries =
    [
        0.0005,
        0.001,
        0.0025,
        0.005,
        0.01,
        0.025,
        0.05,
        0.1,
        0.25,
        0.5,
        1,
        2.5,
        5,
        10,
        30,
        60,
    ];

    public static OnuObservabilityBuilder AddOnuObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var existing = services
            .FirstOrDefault(descriptor => descriptor.ServiceType == typeof(OnuObservabilityBuilder))
            ?.ImplementationInstance as OnuObservabilityBuilder;
        if (existing is not null)
        {
            return existing;
        }

        services.AddOnuObservabilityConfiguration(configuration);
        var effective = ObservabilityConfiguration.CreateEffective(
            ObservabilityConfiguration.BindEffectiveOptions(configuration));
        var resourceIdentity = new TelemetryResourceIdentity(effective);
        var instruments = new OnuTelemetryInstruments();
        var pipelineHealth = new TelemetryPipelineHealth(instruments, TimeProvider.System);
        var exportHealth = new ExportHealthObserverRegistry();
        exportHealth.Add(pipelineHealth);
        var logFlusher = new LogProcessorFlusher();
        var safeLogs = CreateSafeLogPolicyRegistry();
        var schemaBuilder = CreateSchemaBuilder();
        var builder = new OnuObservabilityBuilder(
            services,
            schemaBuilder,
            safeLogs,
            exportHealth,
            configuration);
        services.AddSingleton(builder);
        services.TryAddSingleton(_ => builder.BuildSchema());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(resourceIdentity);
        services.TryAddSingleton(instruments);
        services.TryAddSingleton(pipelineHealth);
        services.TryAddSingleton(exportHealth);
        services.TryAddSingleton<IExportHealthObserver>(exportHealth);
        services.TryAddSingleton(logFlusher);
        services.TryAddSingleton(safeLogs);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPackageTelemetryFlusher>(logFlusher));
        services.TryAddSingleton<SafeEventRateLimiter>();
        services.TryAddSingleton<IOperationSink, OpenTelemetryOperationSink>();
        services.TryAddSingleton<IOperationFactory, OperationFactory>();
        services.TryAddSingleton<IBackgroundOperationOutcomeClassifier, BackgroundOperationOutcomeClassifier>();
        services.TryAddSingleton<IBackgroundOperationRunner, BackgroundOperationRunner>();
        if (effective.LocalJsonLoggingEnabled)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider>(
                new SafeJsonLoggerProvider(
                    Console.Error,
                    effective.MaximumAttributeLength,
                    safeLogs)));
        }

        ConfigureOpenTelemetry(services, effective, resourceIdentity, exportHealth);
        ConfigureLogging(
            services,
            effective,
            resourceIdentity,
            exportHealth,
            logFlusher,
            safeLogs);
        if (effective.Enabled)
        {
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IPackageTelemetryFlusher, TracerProviderFlusher>());
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IPackageTelemetryFlusher, MeterProviderFlusher>());
            services.AddHostedService<TelemetryShutdownService>();
        }
        return builder;
    }

    private static void ConfigureLogging(
        IServiceCollection services,
        EffectiveObservabilitySettings settings,
        TelemetryResourceIdentity resourceIdentity,
        IExportHealthObserver pipelineHealth,
        LogProcessorFlusher logFlusher,
        SafeLogPolicyRegistry safeLogs)
    {
        services.AddLogging(logging =>
        {
            if (!settings.Enabled)
            {
                return;
            }

            logging.AddFilter<OpenTelemetryLoggerProvider>(
                (category, level) =>
                    safeLogs.IsCategoryAllowed(category) &&
                    level >= LogLevel.Information);
            logging.AddOpenTelemetry(options =>
            {
                options.IncludeFormattedMessage = false;
                options.IncludeScopes = true;
                options.ParseStateValues = true;
                if (settings.ExporterEnabled && settings.ExportLogs && settings.Endpoint is { } endpoint)
                {
                    var processor = new BoundedBatchExportProcessor<LogRecord>(
                        new GuardedExporter<LogRecord>(
                            new OtlpLogExporter(CreateExporterOptions(
                                settings,
                                endpoint,
                                "log",
                                pipelineHealth,
                                TelemetrySignal.Log)),
                            pipelineHealth,
                            TelemetrySignal.Log),
                        pipelineHealth,
                        TelemetrySignal.Log,
                        maximumQueueSize: settings.QueueCapacity,
                        scheduledDelayMilliseconds: (int)settings.BatchDelay.TotalMilliseconds,
                        exporterTimeoutMilliseconds: (int)settings.ExportTimeout.TotalMilliseconds,
                        maximumBatchSize: settings.MaximumExportBatchSize);
                    logFlusher.SetProcessor(processor);
                    options.AddProcessor(processor);
                }
            });
        });
        services
            .AddOptions<OpenTelemetryLoggerOptions>()
            .Configure(options =>
                options.SetResourceBuilder(Clone(resourceIdentity.CreateLogResource())));
    }

    private static TelemetrySchemaBuilder CreateSchemaBuilder()
    {
        var core = CoreTelemetrySchema.Create();
        var builder = new TelemetrySchemaBuilder("onu.composed", core.Version);
        foreach (var operation in core.Operations)
        {
            builder.AddOperation(operation);
        }

        foreach (var eventDescriptor in core.Events)
        {
            builder.AddEvent(eventDescriptor);
        }

        foreach (var outcome in core.Outcomes)
        {
            builder.AddOutcome(outcome);
        }

        foreach (var error in core.Errors)
        {
            builder.AddError(error);
        }

        foreach (var reason in core.Reasons)
        {
            builder.AddReason(reason);
        }

        builder
            .AddOutcome(BackgroundOperationSchema.PartialOutcome)
            .AddOutcome(BackgroundOperationSchema.DeferredOutcome)
            .AddError(BackgroundOperationSchema.ValidationError)
            .AddError(BackgroundOperationSchema.DependencyError)
            .AddError(BackgroundOperationSchema.InternalError)
            .AddReason(BackgroundOperationSchema.HostStoppingReason)
            .AddReason(BackgroundOperationSchema.CallerCancellationReason)
            .AddReason(BackgroundOperationSchema.DeadlineReason);

        return builder;
    }

    private static SafeLogPolicyRegistry CreateSafeLogPolicyRegistry()
    {
        var registry = new SafeLogPolicyRegistry();
        registry.Add(new SafeLogPolicy(
            OnuObservabilityDefaults.SafeLogCategory,
            [
                new SafeLogEventPolicy(
                    OnuLogEvents.OperationCompletedEventId,
                    "onu.operation.completed",
                    ["OperationName", "Outcome", "DurationMilliseconds"]),
                new SafeLogEventPolicy(
                    OnuLogEvents.OperationFailedEventId,
                    "onu.operation.failed",
                    ["OperationName", "Outcome", "ErrorCode"]),
                new SafeLogEventPolicy(
                    OnuLogEvents.SuppressionSummaryEventId,
                    "onu.logs.suppressed",
                    ["SuppressedCount", "EventName"]),
            ]));
        return registry;
    }

    private static void ConfigureOpenTelemetry(
        IServiceCollection services,
        EffectiveObservabilitySettings settings,
        TelemetryResourceIdentity resourceIdentity,
        IExportHealthObserver pipelineHealth)
    {
        if (!settings.Enabled)
        {
            return;
        }

        services
            .AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(OnuTelemetrySources.ActivitySourceName)
                    .SetSampler(new ParentBasedSampler(
                        new TraceIdRatioBasedSampler(settings.TraceSamplingRatio)));
                if (settings.ExporterEnabled && settings.ExportTraces && settings.Endpoint is { } endpoint)
                {
                    var exporter = CreateExporterOptions(
                        settings,
                        endpoint,
                        "trace",
                        pipelineHealth,
                        TelemetrySignal.Trace);
                    tracing.AddProcessor(new BoundedBatchExportProcessor<Activity>(
                        new GuardedExporter<Activity>(
                            new OtlpTraceExporter(exporter),
                            pipelineHealth,
                            TelemetrySignal.Trace),
                        pipelineHealth,
                        TelemetrySignal.Trace,
                        maximumQueueSize: settings.QueueCapacity,
                        scheduledDelayMilliseconds: (int)settings.BatchDelay.TotalMilliseconds,
                        exporterTimeoutMilliseconds: (int)settings.ExportTimeout.TotalMilliseconds,
                        maximumBatchSize: settings.MaximumExportBatchSize));
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(OnuTelemetrySources.MeterName)
                    .AddView(
                        "onu.operation.duration",
                        new ExplicitBucketHistogramConfiguration { Boundaries = DurationBoundaries });
                if (settings.ExporterEnabled && settings.ExportMetrics && settings.Endpoint is { } endpoint)
                {
                    var exporter = CreateExporterOptions(
                        settings,
                        endpoint,
                        "metric",
                        pipelineHealth,
                        TelemetrySignal.Metric);
                    metrics.AddReader(new PeriodicExportingMetricReader(
                        new GuardedExporter<Metric>(
                            new OtlpMetricExporter(exporter),
                            pipelineHealth,
                            TelemetrySignal.Metric),
                        exportIntervalMilliseconds: (int)settings.BatchDelay.TotalMilliseconds,
                        exportTimeoutMilliseconds: (int)settings.ExportTimeout.TotalMilliseconds));
                }
            });

        services.ConfigureOpenTelemetryTracerProvider(tracing =>
            tracing.SetResourceBuilder(Clone(resourceIdentity.CreateTraceResource())));
        services.ConfigureOpenTelemetryMeterProvider(metrics =>
            metrics.SetResourceBuilder(Clone(resourceIdentity.CreateMetricResource())));
    }

    private static ResourceBuilder Clone(Resource resource) =>
        ResourceBuilder.CreateEmpty().AddAttributes(resource.Attributes);

    internal static OtlpExporterOptions CreateExporterOptions(
        EffectiveObservabilitySettings settings,
        Uri endpoint,
        string signal,
        IExportHealthObserver? health = null,
        TelemetrySignal telemetrySignal = TelemetrySignal.Trace)
    {
        var protocol = settings.ExporterProtocol == OtlpTransportProtocol.HttpProtobuf
            ? OtlpExportProtocol.HttpProtobuf
            : OtlpExportProtocol.Grpc;
        var options = new OtlpExporterOptions
        {
            Endpoint = protocol == OtlpExportProtocol.HttpProtobuf
                ? SignalEndpoint(endpoint, signal)
                : endpoint,
            Protocol = protocol,
            TimeoutMilliseconds = (int)settings.ExportTimeout.TotalMilliseconds,
        };
        if (health is not null)
        {
            options.HttpClientFactory = () => new HttpClient(
                new TelemetryHttpMessageHandler(
                    health,
                    telemetrySignal,
                    new SocketsHttpHandler()))
            {
                Timeout = settings.ExportTimeout,
            };
        }

        return options;
    }

    internal static Uri SignalEndpoint(Uri endpoint, string signal)
    {
        var path = signal switch
        {
            "trace" => "v1/traces",
            "metric" => "v1/metrics",
            "log" => "v1/logs",
            _ => throw new ArgumentOutOfRangeException(nameof(signal)),
        };
        var absolute = endpoint.AbsoluteUri.TrimEnd('/');
        return absolute.EndsWith('/' + path, StringComparison.OrdinalIgnoreCase)
            ? endpoint
            : new Uri($"{absolute}/{path}", UriKind.Absolute);
    }
}
