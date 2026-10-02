using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace OnuObservability.Hosting;

public static class OnuOutboundHttpClientExtensions
{
    private static readonly string[] SensitiveHttpTagNames =
    [
        "url.full",
        "url.path",
        "url.query",
        "url.scheme",
        "server.address",
        "server.port",
        "http.url",
        "http.target",
        "http.host",
        "http.request.header.authorization",
    ];

    public static OnuObservabilityBuilder AddOutboundHttpClientInstrumentation(
        this OnuObservabilityBuilder builder,
        Func<HttpRequestMessage, bool> requestFilter)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(requestFilter);

        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddHttpClientInstrumentation(options =>
            {
                options.FilterHttpRequestMessage = requestFilter;
                options.EnrichWithHttpRequestMessage = static (activity, _) => Sanitize(activity);
                options.EnrichWithHttpResponseMessage = static (activity, _) => Sanitize(activity);
                options.EnrichWithException = static (activity, _) => Sanitize(activity);
            }));

        return builder;
    }

    private static void Sanitize(Activity activity)
    {
        foreach (var key in SensitiveHttpTagNames)
        {
            activity.SetTag(key, null);
        }
    }
}
