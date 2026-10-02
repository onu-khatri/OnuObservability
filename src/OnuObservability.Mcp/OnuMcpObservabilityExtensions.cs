using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OnuObservability.Hosting;
using OnuObservability.Hosting.Logging;
using OnuObservability.Model;
using OnuObservability.Schema;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace OnuObservability.Mcp;

public static class OnuMcpObservabilityExtensions
{
    public static IMcpServerBuilder WithOnuObservability(
        this IMcpServerBuilder mcpBuilder,
        OnuObservabilityBuilder observability,
        Action<McpObservabilityOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(mcpBuilder);
        ArgumentNullException.ThrowIfNull(observability);

        var existing = mcpBuilder.Services
            .FirstOrDefault(descriptor => descriptor.ServiceType == typeof(McpFilterRuntime))
            ?.ImplementationInstance as McpFilterRuntime;
        if (existing is not null)
        {
            return mcpBuilder;
        }

        var options = new McpObservabilityOptions();
        observability.Configuration
            .GetSection("OnuObservability:Mcp")
            .Bind(options);
        configure?.Invoke(options);
        Validate(options);

        AddSchema(observability);
        var runtime = new McpFilterRuntime();
        var instruments = new McpTelemetryInstruments();
        observability.AddExportHealthObserver(
            new McpExportHealthObserver(instruments, TimeProvider.System));
        var outcomeContext = new McpTelemetryOutcomeContext();
        var classifier = new McpTelemetryOutcomeClassifier();
        mcpBuilder.Services.TryAddSingleton(runtime);
        mcpBuilder.Services.TryAddSingleton(instruments);
        mcpBuilder.Services.TryAddSingleton<IMcpDependencyFailureRecorder>(
            new McpDependencyFailureRecorder(instruments));
        mcpBuilder.Services.TryAddSingleton(options);
        mcpBuilder.Services.TryAddSingleton<IMcpTelemetryOutcomeContext>(outcomeContext);
        mcpBuilder.Services.TryAddSingleton<IMcpTelemetryOutcomeClassifier>(classifier);
        mcpBuilder.Services.TryAddSingleton<McpRegisteredToolRegistry>();
        mcpBuilder.Services.TryAddSingleton<McpRequestTelemetryFilter>();
        mcpBuilder.Services.TryAddSingleton<McpToolTelemetryFilter>();
        if (options.StdioSafeLogging)
        {
            mcpBuilder.Services.Configure<LoggerFilterOptions>(filter =>
                filter.Rules.Add(new LoggerFilterRule(
                    "Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider",
                    categoryName: null,
                    LogLevel.None,
                    filter: null)));
        }

        mcpBuilder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, McpFilterRuntimeInitializer>());
        mcpBuilder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(McpTelemetrySchema.ActivitySourceName))
            .WithMetrics(AddMetrics);

        mcpBuilder.WithMessageFilters(filters =>
            filters.AddIncomingFilter(next => (context, cancellationToken) =>
                runtime.InvokeRequestAsync(next, context, cancellationToken)));
        mcpBuilder.WithRequestFilters(filters =>
            filters.AddCallToolFilter(next => (context, cancellationToken) =>
                runtime.InvokeToolAsync(next, context, cancellationToken)));
        return mcpBuilder;
    }

    private static void AddMetrics(MeterProviderBuilder metrics)
    {
        metrics
            .AddMeter(McpTelemetrySchema.MeterName)
            .AddView(
                "mcp.server.request.duration",
                new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = McpTelemetrySchema.LatencyHistogramBoundaries.ToArray(),
                })
            .AddView(
                "mcp.tool.duration",
                new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = McpTelemetrySchema.LatencyHistogramBoundaries.ToArray(),
                })
            .AddView(
                "mcp.tool.input.items",
                new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = McpTelemetrySchema.ItemHistogramBoundaries.ToArray(),
                })
            .AddView(
                "mcp.tool.output.items",
                new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = McpTelemetrySchema.ItemHistogramBoundaries.ToArray(),
                })
            .AddView(
                "mcp.tool.input.size",
                new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = McpTelemetrySchema.SizeHistogramBoundaries.ToArray(),
                })
            .AddView(
                "mcp.tool.output.size",
                new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = McpTelemetrySchema.SizeHistogramBoundaries.ToArray(),
                });
    }

    private static void AddSchema(OnuObservabilityBuilder builder)
    {
        builder
            .AddOutcome(McpTelemetrySchema.ClientCancelledOutcome)
            .AddOutcome(McpTelemetrySchema.ToolErrorOutcome)
            .AddOutcome(McpTelemetrySchema.ProtocolErrorOutcome);
        foreach (var eventDescriptor in McpTelemetrySchema.Events)
        {
            builder.AddEvent(new EventDescriptor(
                eventDescriptor.Id,
                eventDescriptor.Name,
                eventDescriptor.Name));
        }

        foreach (var code in McpTelemetryValues.RegisteredErrorCodes)
        {
            builder.AddError(new ErrorCode(code));
        }

        builder.AddSafeLogPolicy(new SafeLogPolicy(
            McpTelemetrySchema.SafeLogCategory,
            [
                new SafeLogEventPolicy(
                    McpSafeLogEvents.ToolCompletedEventId,
                    "mcp.tool.completed",
                    ["ToolName", "Outcome", "DurationSeconds", "InputItems", "OutputItems"]),
                new SafeLogEventPolicy(
                    McpSafeLogEvents.ToolFailedEventId,
                    "mcp.tool.failed",
                    ["ToolName", "Outcome", "ErrorType", "DurationSeconds", "InputItems", "OutputItems"]),
                new SafeLogEventPolicy(
                    McpSafeLogEvents.LogsSuppressedEventId,
                    "mcp.logs.suppressed",
                    ["SuppressedEventName", "ToolName", "Outcome", "Count"]),
                new SafeLogEventPolicy(
                    McpSafeLogEvents.DestructiveToolAttemptEventId,
                    "mcp.security.destructive_tool_attempt",
                    ["ToolName", "Outcome"]),
            ]));
    }

    private static void Validate(McpObservabilityOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaximumInputItems, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaximumInputItems, 10_000);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaximumOutputItems, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaximumOutputItems, 10_000);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.LogRateLimitBurst, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.LogRateLimitBurst, 10_000);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.LogRateLimitWindowSeconds, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.LogRateLimitWindowSeconds, 86_400);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaximumLogRateLimitKeys, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaximumLogRateLimitKeys, 10_000);
    }
}
