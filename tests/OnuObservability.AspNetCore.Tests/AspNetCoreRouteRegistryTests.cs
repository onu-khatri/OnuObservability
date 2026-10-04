using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;
using OnuObservability.AspNetCore.Configuration;
using OnuObservability.AspNetCore.Schema;
using OnuObservability.AspNetCore.Telemetry;

namespace OnuObservability.AspNetCore.Tests;

public sealed class AspNetCoreRouteRegistryTests
{
    [Fact]
    public void DiscoversAtMost1024RoutesAndWarnsWithCountsOnly()
    {
        var endpoints = Enumerable.Range(0, 1025)
            .Select(index => Endpoint($"/route-{index:D4}"))
            .ToArray();
        var logger = new CapturingLogger();
        var registry = new AspNetCoreRouteRegistry(
            [new DefaultEndpointDataSource(endpoints)],
            new AspNetCoreObservabilityOptions(),
            logger);

        var retained = registry.Resolve(endpoints[0]);
        var overflow = registry.Resolve(endpoints[^1]);

        Assert.Equal("/route-0000", retained.RouteTemplate);
        Assert.Equal(AspNetCoreTelemetrySchema.UnknownRoute, overflow.RouteTemplate);
        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("1025", warning.Message, StringComparison.Ordinal);
        Assert.Contains("1024", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("/route-", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExactConfigurationExclusionPreventsAdapterTelemetry()
    {
        var endpoint = Endpoint("/health");
        var options = new AspNetCoreObservabilityOptions
        {
            ExcludedRouteTemplates = ["/health"],
        };
        var registry = new AspNetCoreRouteRegistry(
            [new DefaultEndpointDataSource(endpoint)],
            options,
            new CapturingLogger());

        var resolution = registry.Resolve(endpoint);

        Assert.True(resolution.IsExcluded);
        Assert.Equal("/health", resolution.RouteTemplate);
    }

    private static RouteEndpoint Endpoint(string route) => new(
        _ => Task.CompletedTask,
        RoutePatternFactory.Parse(route),
        order: 0,
        EndpointMetadataCollection.Empty,
        route);

    private sealed class CapturingLogger : ILogger<AspNetCoreRouteRegistry>
    {
        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new(logLevel, formatter(state, exception)));
    }

    private sealed record Entry(LogLevel Level, string Message);
}
