using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace OnuObservability.AspNetCore;

internal sealed partial class AspNetCoreRouteRegistry
{
    private readonly IEnumerable<EndpointDataSource> _dataSources;
    private readonly HashSet<string> _excluded;
    private readonly ILogger<AspNetCoreRouteRegistry> _logger;
    private readonly int _maximumRoutes;
    private readonly object _sync = new();
    private HashSet<string>? _routes;

    public AspNetCoreRouteRegistry(
        IEnumerable<EndpointDataSource> dataSources,
        AspNetCoreObservabilityOptions options,
        ILogger<AspNetCoreRouteRegistry> logger)
    {
        _dataSources = dataSources;
        _maximumRoutes = options.MaximumRouteTemplates;
        _excluded = options.ExcludedRouteTemplates.ToHashSet(StringComparer.Ordinal);
        _logger = logger;
    }

    public RouteResolution Resolve(Endpoint? endpoint)
    {
        var candidate = (endpoint as RouteEndpoint)?.RoutePattern.RawText;
        if (candidate is null)
        {
            return new(RouteTemplate: AspNetCoreTelemetrySchema.UnknownRoute, IsExcluded: false);
        }

        if (_excluded.Contains(candidate))
        {
            return new(candidate, IsExcluded: true);
        }

        var routes = EnsureRoutes();
        return routes.Contains(candidate)
            ? new(candidate, IsExcluded: false)
            : new(AspNetCoreTelemetrySchema.UnknownRoute, IsExcluded: false);
    }

    private HashSet<string> EnsureRoutes()
    {
        if (Volatile.Read(ref _routes) is { } current)
        {
            return current;
        }

        lock (_sync)
        {
            if (_routes is not null)
            {
                return _routes;
            }

            var discovered = _dataSources
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .Select(endpoint => endpoint.RoutePattern.RawText)
                .Where(route => route is not null && !_excluded.Contains(route))
                .Select(route => route!)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            _routes = discovered.Take(_maximumRoutes).ToHashSet(StringComparer.Ordinal);
            if (discovered.Length > _maximumRoutes)
            {
                RouteLimitExceeded(_logger, discovered.Length, _maximumRoutes);
            }

            return _routes;
        }
    }

    [LoggerMessage(
        EventId = 1201,
        Level = LogLevel.Warning,
        Message = "ASP.NET Core route template limit exceeded: discovered={DiscoveredCount} retained={RetainedCount}")]
    private static partial void RouteLimitExceeded(
        ILogger logger,
        int discoveredCount,
        int retainedCount);
}

internal readonly record struct RouteResolution(string RouteTemplate, bool IsExcluded);
