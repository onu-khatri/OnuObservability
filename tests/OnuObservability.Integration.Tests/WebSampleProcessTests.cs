using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;

namespace OnuObservability.Integration.Tests;

public sealed class WebSampleProcessTests
{
    [Fact]
    public async Task PackedWebPreservesPropagationPrivacyAndBoundedOutageShutdown()
    {
        const string traceId = "4bf92f3577b34da6a3ce929d0e0e4736";
        const string sensitiveRouteValue = SensitiveDataCorpus.Canary;
        var assembly = SampleAssemblyResolver.Resolve(
            "ONU_WEB_SAMPLE_DLL",
            "OnuObservability.Sample.Web",
            "OnuObservability.Sample.Web.dll");
        var port = ReserveTcpPort();
        var address = new Uri($"http://127.0.0.1:{port}");
        var start = new ProcessStartInfo("dotnet")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(assembly)!,
        };
        start.ArgumentList.Add(assembly);
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add(address.ToString());
        start.Environment["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://127.0.0.1:1";
        start.Environment["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf";
        start.Environment["OTEL_TRACES_EXPORTER"] = "otlp";
        start.Environment["OTEL_METRICS_EXPORTER"] = "otlp";
        start.Environment["OTEL_LOGS_EXPORTER"] = "otlp";
        start.Environment["OnuObservability__BatchDelayMilliseconds"] = "50";
        start.Environment["OnuObservability__ExportTimeoutMilliseconds"] = "100";
        start.Environment["OnuObservability__ShutdownFlushTimeoutMilliseconds"] = "1000";
        start.Environment["OnuObservability__QueueCapacity"] = "16";
        start.Environment["OnuObservability__MaximumExportBatchSize"] = "8";

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("The Web sample process could not be started.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var client = new HttpClient
        {
            BaseAddress = address,
            Timeout = TimeSpan.FromSeconds(2),
        };

        try
        {
            await WaitUntilReadyAsync(client, process);

            using var correlationRequest = new HttpRequestMessage(HttpMethod.Get, "/correlation");
            correlationRequest.Headers.TryAddWithoutValidation(
                "traceparent",
                $"00-{traceId}-00f067aa0ba902b7-01");
            using var correlationResponse = await client.SendAsync(correlationRequest);
            correlationResponse.EnsureSuccessStatusCode();
            var correlation = await correlationResponse.Content.ReadFromJsonAsync<Correlation>();
            Assert.Equal(traceId, correlation?.TraceId);
            Assert.Equal(16, correlation?.SpanId?.Length);

            using var userResponse = await client.GetAsync($"/users/{sensitiveRouteValue}");
            userResponse.EnsureSuccessStatusCode();
            using var failureResponse = await client.GetAsync("/failure");
            Assert.Equal(HttpStatusCode.InternalServerError, failureResponse.StatusCode);
            var shutdownStopwatch = Stopwatch.StartNew();
            try
            {
                using var shutdownResponse = await client.PostAsync("/shutdown", content: null);
                Assert.True(
                    shutdownResponse.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.ServiceUnavailable);
            }
            catch (HttpRequestException) when (!process.HasExited)
            {
                // The host may close the loopback connection immediately after accepting shutdown.
            }

            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            shutdownStopwatch.Stop();
            Assert.True(shutdownStopwatch.Elapsed < TimeSpan.FromSeconds(10), shutdownStopwatch.Elapsed.ToString());
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }

        var output = await stdoutTask + await stderrTask;
        Assert.Equal(0, process.ExitCode);
        Assert.DoesNotContain(sensitiveRouteValue, output, StringComparison.Ordinal);
        Assert.Contains("onu.telemetry.dropped", output, StringComparison.Ordinal);
    }

    private static async Task WaitUntilReadyAsync(HttpClient client, Process process)
    {
        for (var attempt = 0; attempt < 150; attempt++)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"The Web sample exited before becoming ready with code {process.ExitCode}.");
            }

            try
            {
                using var response = await client.GetAsync("/");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("The Web sample did not become ready within fifteen seconds.");
    }

    private static int ReserveTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed record Correlation(string? TraceId, string? SpanId);
}
