using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace OnuObservability.Hosting.Tests;

public sealed class OutboundHttpClientInstrumentationTests
{
    [Fact]
    public async Task ReviewedRequestsAreTracedWithoutRawAddressMetadata()
    {
        var activities = new List<Activity>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OnuObservability:ServiceName"] = "http-client-test",
                ["OnuObservability:ExporterEnabled"] = "false",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddOnuObservability(configuration)
            .AddOutboundHttpClientInstrumentation(static request =>
                request.RequestUri?.AbsolutePath == "/allowed");
        services.ConfigureOpenTelemetryTracerProvider(tracing =>
            tracing.AddInMemoryExporter(activities));

        using var root = services.BuildServiceProvider();
        var provider = root.GetRequiredService<TracerProvider>();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var server = ServeOnceAsync(listener);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false });

        await client.GetAsync(
            $"http://127.0.0.1:{endpoint.Port}/allowed?private-canary=secret");
        await server;
        Assert.True(provider.ForceFlush(5_000));

        var activity = Assert.Single(activities, candidate =>
            candidate.Source.Name == "System.Net.Http");
        var exported = string.Join('|', activity.TagObjects.Select(tag =>
            $"{tag.Key}={tag.Value}"));
        Assert.DoesNotContain("private-canary", exported, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/allowed", exported, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", exported, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(activity.TagObjects, tag => tag.Key is
            "url.full" or
            "url.path" or
            "url.query" or
            "server.address" or
            "server.port" or
            "http.url" or
            "http.target" or
            "http.host" or
            "http.request.header.authorization");
    }

    private static async Task ServeOnceAsync(TcpListener listener)
    {
        using var socket = await listener.AcceptTcpClientAsync();
        await using var stream = socket.GetStream();
        using var reader = new StreamReader(
            stream,
            Encoding.ASCII,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
        {
        }

        var response = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(response);
        await stream.FlushAsync();
    }
}
