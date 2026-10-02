using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OnuObservability.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace OnuObservability.AspNetCore;

public static class OnuAspNetCoreExtensions
{
    public static OnuObservabilityBuilder AddOnuAspNetCore(
        this OnuObservabilityBuilder builder,
        Action<AspNetCoreObservabilityOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new AspNetCoreObservabilityOptions();
        builder.Configuration
            .GetSection("OnuObservability:AspNetCore")
            .Bind(options);
        configure?.Invoke(options);
        Validate(options);

        builder
            .AddOutcome(AspNetCoreTelemetrySchema.NotFoundOutcome)
            .AddOutcome(AspNetCoreTelemetrySchema.ServerErrorOutcome);
        builder.Services.TryAddSingleton(options);
        builder.Services.TryAddSingleton<AspNetCoreTelemetryInstruments>();
        builder.Services.TryAddSingleton<AspNetCoreRouteRegistry>();
        builder.Services
            .AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddMeter(AspNetCoreTelemetrySchema.MeterName));
        if (options.InstrumentRequests)
        {
            builder.Services
                .AddOpenTelemetry()
                .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation(instrumentation =>
                {
                    instrumentation.RecordException = false;
                }));
        }

        return builder;
    }

    public static IApplicationBuilder UseOnuObservability(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<OnuAspNetCoreMiddleware>();
    }

    private static void Validate(AspNetCoreObservabilityOptions options)
    {
        if (options.MaximumRouteTemplates is < 1 or > 1024)
        {
            throw new InvalidOperationException(
                "MaximumRouteTemplates must be between 1 and 1024.");
        }

        if (options.ExcludedRouteTemplates.Length > 1024)
        {
            throw new InvalidOperationException(
                "At most 1024 route template exclusions may be configured.");
        }

        foreach (var route in options.ExcludedRouteTemplates)
        {
            if (string.IsNullOrWhiteSpace(route) || route.Length > 256 || !route.StartsWith('/'))
            {
                throw new InvalidOperationException(
                    "Route templates must start with '/' and contain at most 256 characters.");
            }
        }
    }
}
