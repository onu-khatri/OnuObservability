using System.Diagnostics;
using System.Text.Json;

namespace OnuObservability.Integration.Tests;

public sealed class WorkerSampleProcessTests
{
    [Fact]
    public async Task PackedWorkerRunsEveryScenarioAndStopsWithinBudget()
    {
        var assembly = SampleAssemblyResolver.Resolve(
            "ONU_WORKER_SAMPLE_DLL",
            "OnuObservability.Sample.Worker",
            "OnuObservability.Sample.Worker.dll");
        var start = new ProcessStartInfo("dotnet")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(assembly)!,
        };
        start.ArgumentList.Add(assembly);
        start.ArgumentList.Add("--once");
        start.Environment["OTEL_EXPORTER_OTLP_ENDPOINT"] = TestOtlpEndpoint();
        start.Environment["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf";
        start.Environment["OTEL_TRACES_EXPORTER"] = "otlp";
        start.Environment["OTEL_METRICS_EXPORTER"] = "otlp";
        start.Environment["OTEL_LOGS_EXPORTER"] = "otlp";
        start.Environment["OnuObservability__BatchDelayMilliseconds"] = "50";
        start.Environment["OnuObservability__ExportTimeoutMilliseconds"] = "100";
        start.Environment["OnuObservability__ShutdownFlushTimeoutMilliseconds"] = "1000";
        start.Environment["OnuObservability__QueueCapacity"] = "16";
        start.Environment["OnuObservability__MaximumExportBatchSize"] = "8";
        start.Environment["OTEL_RESOURCE_ATTRIBUTES"] =
            $"private.token={SensitiveDataCorpus.Canary}";

        var stopwatch = Stopwatch.StartNew();
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("The Worker sample process could not be started.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }

        stopwatch.Stop();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        Assert.Equal(0, process.ExitCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), stopwatch.Elapsed.ToString());
        var records = stderr.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith('{'))
            .Select(line => JsonDocument.Parse(line))
            .ToArray();
        try
        {
            var outcomes = records
                .Where(record => record.RootElement.TryGetProperty("Fields", out _))
                .Select(record => record.RootElement.GetProperty("Fields"))
                .Where(fields => fields.TryGetProperty("Outcome", out _))
                .Select(fields => fields.GetProperty("Outcome").GetString())
                .ToArray();
            Assert.Contains("success", outcomes);
            Assert.Contains("partial", outcomes);
            Assert.Contains("dependency_error", outcomes);
            Assert.Contains("deadline_exceeded", outcomes);
            Assert.Contains("host_stopped", outcomes);
            Assert.Contains(records, record =>
                record.RootElement.TryGetProperty("Fields", out var fields) &&
                fields.TryGetProperty("OperationName", out var operation) &&
                operation.GetString() == "sample.worker.dependency");
            Assert.Contains(records.GroupBy(record =>
                    record.RootElement.TryGetProperty("TraceId", out var traceId)
                        ? traceId.GetString()
                        : null),
                group => group.Key is { Length: > 0 } && group.Count() >= 2);
            Assert.DoesNotContain(SensitiveDataCorpus.Canary, stdout + stderr, StringComparison.Ordinal);
        }
        finally
        {
            foreach (var record in records)
            {
                record.Dispose();
            }
        }
    }

    private static string TestOtlpEndpoint() =>
        Environment.GetEnvironmentVariable("ONU_TEST_OTLP_ENDPOINT")
        ?? "http://127.0.0.1:1";

}
