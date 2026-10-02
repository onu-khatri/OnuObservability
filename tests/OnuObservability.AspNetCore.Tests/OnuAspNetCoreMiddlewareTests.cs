using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging.Abstractions;

namespace OnuObservability.AspNetCore.Tests;

public sealed class OnuAspNetCoreMiddlewareTests
{
    [Fact]
    public async Task SuccessfulRequestEnrichesExistingActivityAndRecordsBoundedMetrics()
    {
        using var instruments = new AspNetCoreTelemetryInstruments();
        using var metrics = new MetricCapture();
        var context = Context("/users/private-user", "/users/{id}", 200);
        using var activity = new Activity("framework-request").Start();
        var originalId = activity.Id;
        var middleware = new OnuAspNetCoreMiddleware(
            _ => Task.CompletedTask,
            instruments,
            Routes("/users/{id}"),
            TimeProvider.System);

        await middleware.InvokeAsync(context);

        Assert.Equal(originalId, Activity.Current?.Id);
        Assert.Equal("GET /users/{id}", activity.DisplayName);
        Assert.Equal("/users/{id}", activity.GetTagItem("http.route"));
        Assert.Equal("success", activity.GetTagItem("onu.outcome"));
        Assert.Equal(2, metrics.Measurements.Count);
        Assert.All(metrics.Measurements, measurement =>
        {
            Assert.Contains(measurement.Tags, tag =>
                tag.Key == "http.route" && Equals(tag.Value, "/users/{id}"));
            Assert.DoesNotContain(measurement.Tags, tag =>
                Equals(tag.Value, "/users/private-user"));
        });
    }

    [Fact]
    public async Task FailureRemovesSensitiveMetadataAndDoesNotRecordExceptionContent()
    {
        const string canary = "secret-canary";
        using var instruments = new AspNetCoreTelemetryInstruments();
        var context = Context($"/orders/{canary}", "/orders/{orderId}", 500);
        context.Request.QueryString = new QueryString($"?token={canary}");
        using var activity = new Activity("framework-request").Start();
        activity.SetTag("url.path", context.Request.Path.Value);
        activity.SetTag("url.query", context.Request.QueryString.Value);
        activity.SetTag("http.request.header.authorization", canary);
        activity.SetTag("client.address", canary);
        activity.SetTag("exception.message", canary);
        var middleware = new OnuAspNetCoreMiddleware(
            _ => throw new InvalidOperationException(canary),
            instruments,
            Routes("/orders/{orderId}"),
            TimeProvider.System);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => middleware.InvokeAsync(context));

        Assert.Equal(canary, thrown.Message);
        Assert.Equal("server_error", activity.GetTagItem("onu.outcome"));
        Assert.DoesNotContain(
            canary,
            string.Join('|', activity.TagObjects.Select(tag => tag.Value)),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(400, "validation_error")]
    [InlineData(404, "not_found")]
    [InlineData(503, "server_error")]
    public async Task StatusCodesMapToStableOutcomes(int statusCode, string expected)
    {
        using var instruments = new AspNetCoreTelemetryInstruments();
        var context = Context("/anything", "/registered", statusCode);
        using var activity = new Activity("framework-request").Start();
        var middleware = new OnuAspNetCoreMiddleware(
            _ => Task.CompletedTask,
            instruments,
            Routes("/registered"),
            TimeProvider.System);

        await middleware.InvokeAsync(context);

        Assert.Equal(expected, activity.GetTagItem("onu.outcome"));
    }

    [Fact]
    public async Task UnregisteredRoutesAndMethodsCollapseToFallbacks()
    {
        using var instruments = new AspNetCoreTelemetryInstruments();
        using var metrics = new MetricCapture();
        var middleware = new OnuAspNetCoreMiddleware(
            _ => Task.CompletedTask,
            instruments,
            Routes("/known"),
            TimeProvider.System);
        var displayNames = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < 2_000; index++)
        {
            var context = Context($"/private/{index}", $"/private/{index}", 200);
            context.Request.Method = $"CUSTOM-{index}";
            using var activity = new Activity("framework-request").Start();
            await middleware.InvokeAsync(context);
            displayNames.Add(activity.DisplayName);
        }

        Assert.All(metrics.Measurements, measurement =>
        {
            Assert.Contains(measurement.Tags, tag =>
                tag.Key == "http.route" && Equals(tag.Value, "other"));
            Assert.Contains(measurement.Tags, tag =>
                tag.Key == "http.request.method" && Equals(tag.Value, "OTHER"));
        });
        Assert.Equal(["OTHER other"], displayNames);
    }

    [Theory]
    [InlineData(true, "caller_cancelled")]
    [InlineData(false, "deadline_exceeded")]
    public async Task CancellationSourceMapsWithoutInspectingExceptionMessages(
        bool abortRequest,
        string expected)
    {
        using var instruments = new AspNetCoreTelemetryInstruments();
        using var aborted = new CancellationTokenSource();
        var context = Context("/work", "/work", 200);
        context.RequestAborted = aborted.Token;
        if (abortRequest)
        {
            aborted.Cancel();
        }

        using var activity = new Activity("framework-request").Start();
        var middleware = new OnuAspNetCoreMiddleware(
            _ => throw new OperationCanceledException("private-message"),
            instruments,
            Routes("/work"),
            TimeProvider.System);

        await Assert.ThrowsAsync<OperationCanceledException>(() => middleware.InvokeAsync(context));

        Assert.Equal(expected, activity.GetTagItem("onu.outcome"));
        Assert.DoesNotContain(
            "private-message",
            string.Join('|', activity.TagObjects.Select(tag => tag.Value)),
            StringComparison.Ordinal);
    }

    private static AspNetCoreRouteRegistry Routes(params string[] routes)
    {
        var options = new AspNetCoreObservabilityOptions();
        var endpoints = routes.Select(route => (Endpoint)new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(route),
            order: 0,
            EndpointMetadataCollection.Empty,
            route)).ToArray();
        return new AspNetCoreRouteRegistry(
            [new DefaultEndpointDataSource(endpoints)],
            options,
            NullLogger<AspNetCoreRouteRegistry>.Instance);
    }

    private static DefaultHttpContext Context(
        string path,
        string routeTemplate,
        int statusCode)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = path;
        context.Response.StatusCode = statusCode;
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(routeTemplate),
            order: 0,
            EndpointMetadataCollection.Empty,
            routeTemplate));
        return context;
    }

    private sealed class MetricCapture : IDisposable
    {
        private readonly MeterListener _listener = new();

        public MetricCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == AspNetCoreTelemetrySchema.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
                Measurements.Add(new(instrument.Name, value, tags.ToArray())));
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
                Measurements.Add(new(instrument.Name, value, tags.ToArray())));
            _listener.Start();
        }

        public List<Measurement> Measurements { get; } = [];

        public void Dispose() => _listener.Dispose();
    }

    private sealed record Measurement(
        string Name,
        double Value,
        KeyValuePair<string, object?>[] Tags);
}
