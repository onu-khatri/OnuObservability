using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OnuObservability.Hosting.Composition;
using OnuObservability.Operations;
using OnuObservability.Schema;

const int warmupOperations = 2_000;
const int measuredOperations = 25_000;
var output = Argument(args, "--output") ?? Path.Combine(
    AppContext.BaseDirectory,
    "onu-observability-benchmark.json");
var results = new[]
{
    Measure("disabled", enabled: false, exporterEnabled: false),
    Measure("local-only", enabled: true, exporterEnabled: false),
    Measure("batched-otlp", enabled: true, exporterEnabled: true),
};
var report = new
{
    generatedAtUtc = DateTimeOffset.UtcNow,
    runtime = Environment.Version.ToString(),
    processorCount = Environment.ProcessorCount,
    warmupOperations,
    measuredOperations,
    results,
    passed = results.All(result => result.Passed),
};
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
await File.WriteAllTextAsync(
    output,
    JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
return report.passed ? 0 : 1;

static BenchmarkResult Measure(string name, bool enabled, bool exporterEnabled)
{
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OnuObservability:Enabled"] = enabled.ToString(),
            ["OnuObservability:ExporterEnabled"] = exporterEnabled.ToString(),
            ["OnuObservability:ServiceName"] = "onu-benchmark",
            ["OnuObservability:Endpoint"] = "http://127.0.0.1:1",
            ["OnuObservability:ExporterProtocol"] = "http/protobuf",
            ["OnuObservability:ExportLogs"] = "false",
            ["OnuObservability:ExportTimeoutMilliseconds"] = "10",
            ["OnuObservability:BatchDelayMilliseconds"] = "10",
            ["OnuObservability:ShutdownFlushTimeoutMilliseconds"] = "100",
            ["OnuObservability:QueueCapacity"] = "65536",
            ["OnuObservability:MaximumExportBatchSize"] = "512",
        })
        .Build();
    var descriptor = new OperationDescriptor("benchmark.operation");
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddOnuObservability(configuration).AddOperation(descriptor);
    using var provider = services.BuildServiceProvider();
    var factory = provider.GetRequiredService<IOperationFactory>();

    for (var index = 0; index < warmupOperations; index++)
    {
        Execute(factory, descriptor);
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var samples = new long[measuredOperations];
    var process = Process.GetCurrentProcess();
    var cpuStarted = process.TotalProcessorTime;
    var allocatedStarted = GC.GetAllocatedBytesForCurrentThread();
    var wall = Stopwatch.StartNew();
    for (var index = 0; index < measuredOperations; index++)
    {
        var started = Stopwatch.GetTimestamp();
        Execute(factory, descriptor);
        samples[index] = Stopwatch.GetTimestamp() - started;
    }

    wall.Stop();
    var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedStarted;
    var cpu = (Process.GetCurrentProcess().TotalProcessorTime - cpuStarted).TotalMilliseconds;
    Array.Sort(samples);
    var p50 = Microseconds(samples, 0.50);
    var p95 = Microseconds(samples, 0.95);
    var p99 = Microseconds(samples, 0.99);
    var allocationPerOperation = (double)allocated / measuredOperations;
    var budget = name switch
    {
        "disabled" => new Budget(50, 2_048),
        "local-only" => new Budget(100, 2_048),
        _ => new Budget(150, 4_096),
    };
    return new(
        name,
        p50,
        p95,
        p99,
        measuredOperations / wall.Elapsed.TotalSeconds,
        cpu,
        allocationPerOperation,
        budget.P95Microseconds,
        budget.AllocatedBytesPerOperation,
        p95 <= budget.P95Microseconds && allocationPerOperation <= budget.AllocatedBytesPerOperation);
}

static void Execute(IOperationFactory factory, OperationDescriptor descriptor)
{
    using var operation = factory.Start(descriptor);
    operation.Complete(OperationResult.Success);
}

static double Microseconds(long[] samples, double percentile)
{
    var index = (int)Math.Ceiling(samples.Length * percentile) - 1;
    return samples[Math.Clamp(index, 0, samples.Length - 1)] * 1_000_000d / Stopwatch.Frequency;
}

static string? Argument(string[] arguments, string name)
{
    var index = Array.IndexOf(arguments, name);
    return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
}

internal sealed record Budget(double P95Microseconds, double AllocatedBytesPerOperation);

internal sealed record BenchmarkResult(
    string Scenario,
    double P50Microseconds,
    double P95Microseconds,
    double P99Microseconds,
    double ThroughputOperationsPerSecond,
    double CpuMilliseconds,
    double AllocatedBytesPerOperation,
    double P95BudgetMicroseconds,
    double AllocationBudgetBytesPerOperation,
    bool Passed);
