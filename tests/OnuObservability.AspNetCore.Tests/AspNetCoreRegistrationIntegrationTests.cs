using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OnuObservability.Hosting;
using OpenTelemetry.Trace;

namespace OnuObservability.AspNetCore.Tests;

public sealed class AspNetCoreRegistrationIntegrationTests
{
    [Fact]
    public void AdapterOptionsBindFromOnuObservabilityAspNetCoreConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OnuObservability:Enabled"] = "false",
                ["OnuObservability:ServiceName"] = "web-test",
                ["OnuObservability:AspNetCore:MaximumRouteTemplates"] = "128",
                ["OnuObservability:AspNetCore:ExcludedRouteTemplates:0"] = "/health",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOnuObservability(configuration).AddOnuAspNetCore();
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<AspNetCoreObservabilityOptions>();

        Assert.Equal(128, options.MaximumRouteTemplates);
        Assert.Equal(["/health"], options.ExcludedRouteTemplates);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LogicalRequestProducesOneServerActivity(bool packageOwnsInstrumentation)
    {
        var stopped = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OnuObservability:Enabled"] = "true",
            ["OnuObservability:ExporterEnabled"] = "false",
            ["OnuObservability:ServiceName"] = "web-test",
        });
        if (!packageOwnsInstrumentation)
        {
            builder.Services
                .AddOpenTelemetry()
                .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation());
        }

        builder.Services
            .AddOnuObservability(builder.Configuration)
            .AddOnuAspNetCore(options =>
                options.InstrumentRequests = packageOwnsInstrumentation);
        await using var app = builder.Build();
        app.UseOnuObservability();
        app.MapGet("/health", () => Results.Ok());

        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.Single();
            using var client = new HttpClient();
            using var response = await client.GetAsync(new Uri(address + "/health"));
            response.EnsureSuccessStatusCode();
        }
        finally
        {
            await app.StopAsync();
        }

        var serverActivities = stopped.Where(activity => activity.Kind == ActivityKind.Server).ToArray();
        var server = Assert.Single(serverActivities);
        Assert.Equal("GET /health", server.DisplayName);
        Assert.Equal("/health", server.GetTagItem("http.route"));
    }
}
