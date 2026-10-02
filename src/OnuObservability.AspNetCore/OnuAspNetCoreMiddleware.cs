using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OnuObservability.Model;

namespace OnuObservability.AspNetCore;

internal sealed class OnuAspNetCoreMiddleware(
    RequestDelegate next,
    AspNetCoreTelemetryInstruments instruments,
    AspNetCoreRouteRegistry routes,
    TimeProvider timeProvider)
{
    private static readonly HashSet<string> AllowedMethods = new(
        ["CONNECT", "DELETE", "GET", "HEAD", "OPTIONS", "PATCH", "POST", "PUT", "TRACE"],
        StringComparer.Ordinal);

    public async Task InvokeAsync(HttpContext context)
    {
        var started = timeProvider.GetTimestamp();
        var outcome = CommonOutcomes.Success;
        try
        {
            await next(context).ConfigureAwait(false);
            outcome = FromStatusCode(context.Response.StatusCode);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            outcome = CommonOutcomes.CallerCancelled;
            throw;
        }
        catch (OperationCanceledException)
        {
            outcome = CommonOutcomes.DeadlineExceeded;
            throw;
        }
        catch (Exception)
        {
            outcome = AspNetCoreTelemetrySchema.ServerErrorOutcome;
            throw;
        }
        finally
        {
            Record(context, outcome, timeProvider.GetElapsedTime(started));
        }
    }

    private void Record(HttpContext context, Outcome outcome, TimeSpan duration)
    {
        var routeResolution = routes.Resolve(context.GetEndpoint());
        RemoveSensitiveActivityTags(Activity.Current);
        if (routeResolution.IsExcluded)
        {
            if (Activity.Current is { } excludedActivity)
            {
                excludedActivity.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
                excludedActivity.IsAllDataRequested = false;
            }

            return;
        }

        var route = routeResolution.RouteTemplate;
        var method = NormalizeMethod(context.Request.Method);
        var statusClass = StatusClass(context.Response.StatusCode);
        var tags = new TagList
        {
            { "http.request.method", method },
            { "http.route", route },
            { "http.response.status_class", statusClass },
            { "onu.outcome", outcome.Value },
        };
        instruments.RequestCount.Add(1, tags);
        instruments.RequestDuration.Record(duration.TotalSeconds, tags);

        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        activity.DisplayName = $"{method} {route}";
        activity.SetTag("http.request.method", method);
        activity.SetTag("http.route", route);
        activity.SetTag("http.response.status_class", statusClass);
        activity.SetTag("onu.outcome", outcome.Value);
        activity.SetStatus(
            outcome == CommonOutcomes.Success ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
    }

    private static string NormalizeMethod(string method) =>
        AllowedMethods.Contains(method) ? method : "OTHER";

    private static string StatusClass(int statusCode) => statusCode switch
    {
        >= 100 and < 200 => "1xx",
        >= 200 and < 300 => "2xx",
        >= 300 and < 400 => "3xx",
        >= 400 and < 500 => "4xx",
        >= 500 and < 600 => "5xx",
        _ => "other",
    };

    private static Outcome FromStatusCode(int statusCode) => statusCode switch
    {
        404 => AspNetCoreTelemetrySchema.NotFoundOutcome,
        400 or 409 or 422 => CommonOutcomes.ValidationError,
        >= 500 => AspNetCoreTelemetrySchema.ServerErrorOutcome,
        >= 400 => CommonOutcomes.Other,
        _ => CommonOutcomes.Success,
    };

    private static void RemoveSensitiveActivityTags(Activity? activity)
    {
        if (activity is null)
        {
            return;
        }

        foreach (var tag in activity.TagObjects.ToArray())
        {
            if (IsSensitiveTag(tag.Key))
            {
                activity.SetTag(tag.Key, null);
            }
        }
    }

    private static bool IsSensitiveTag(string name) =>
        name is "url.path" or "url.query" or "url.full" or "http.target" or "http.url" or
            "client.address" or "client.port" or "network.peer.address" or "network.peer.port" or
            "user_agent.original" ||
        name.StartsWith("http.request.header.", StringComparison.Ordinal) ||
        name.StartsWith("http.response.header.", StringComparison.Ordinal) ||
        name.StartsWith("exception.", StringComparison.Ordinal);
}
