using OnuObservability.Hosting.Background;
using OnuObservability.Hosting.Composition;
using OnuObservability.Sample.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddOnuObservability(builder.Configuration)
    .AddWorkerTelemetry();
builder.Services.AddSingleton<SampleWorkload>();
var runOnce = args.Contains("--once", StringComparer.Ordinal);
if (!runOnce)
{
    builder.Services.AddHostedService<Worker>();
}

var host = builder.Build();
if (!runOnce)
{
    await host.RunAsync();
    return;
}

await host.StartAsync();
var workload = host.Services.GetRequiredService<SampleWorkload>();
foreach (var scenario in new[]
         {
             SampleWorkerScenario.Success,
             SampleWorkerScenario.Partial,
             SampleWorkerScenario.DependencyOutage,
             SampleWorkerScenario.Deadline,
         })
{
    try
    {
        await workload.RunAsync(scenario, default);
    }
    catch (BackgroundOperationException)
    {
        // Expected deterministic dependency-outage scenario.
    }
    catch (OperationCanceledException)
    {
        // Expected deterministic deadline scenario.
    }
}

using var stopping = new CancellationTokenSource();
stopping.Cancel();
try
{
    await workload.RunAsync(SampleWorkerScenario.GracefulStop, stopping.Token);
}
catch (OperationCanceledException)
{
    // Expected deterministic host-stop scenario.
}

await host.StopAsync();
