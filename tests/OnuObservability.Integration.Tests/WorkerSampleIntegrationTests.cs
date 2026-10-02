using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OnuObservability.Hosting;
using OnuObservability.Hosting.Background;
using OnuObservability.Operations;
using OnuObservability.Sample.Worker;

namespace OnuObservability.Integration.Tests;

public sealed class WorkerSampleIntegrationTests
{
    [Fact]
    public async Task SampleUsesPublicRegistrationAndEmitsBoundedCorrelatedOperations()
    {
        var sink = new RecordingSink();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OnuObservability:Enabled"] = "false",
                ["OnuObservability:ServiceName"] = "worker-test",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOperationSink>(sink);
        services.AddOnuObservability(configuration).AddWorkerTelemetry();
        services.AddSingleton<SampleWorkload>();
        await using var provider = services.BuildServiceProvider();
        var workload = provider.GetRequiredService<SampleWorkload>();

        await workload.RunAsync(SampleWorkerScenario.Success, default);
        await workload.RunAsync(SampleWorkerScenario.Partial, default);
        await Assert.ThrowsAsync<BackgroundOperationException>(async () =>
            await workload.RunAsync(SampleWorkerScenario.DependencyOutage, default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await workload.RunAsync(SampleWorkerScenario.Deadline, default));
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await workload.RunAsync(SampleWorkerScenario.GracefulStop, stopping.Token));

        Assert.Equal(6, sink.Completed.Count);
        Assert.Equal(5, sink.Completed.Count(value => value.Descriptor == WorkerTelemetry.Pass));
        var dependency = Assert.Single(
            sink.Completed,
            value => value.Descriptor == WorkerTelemetry.Dependency);
        Assert.Equal(WorkerTelemetry.Pass, dependency.ParentDescriptor);
        Assert.All(
            sink.Completed.Where(value => value.Descriptor == WorkerTelemetry.Pass),
            operation => Assert.True(operation.Fields.Count is 0 or 6));
        Assert.Contains(sink.Completed, value => value.Result.Outcome.Value == "partial");
        Assert.Contains(sink.Completed, value => value.Result.Outcome.Value == "dependency_error");
        Assert.Contains(sink.Completed, value => value.Result.Outcome.Value == "deadline_exceeded");
        Assert.Contains(sink.Completed, value => value.Result.Outcome.Value == "host_stopped");
    }

    private sealed class RecordingSink : IOperationSink
    {
        public ConcurrentBag<OperationCompleted> Completed { get; } = [];

        public IOperationObserver Start(OperationStarted operation) => new Observer(Completed);

        private sealed class Observer(ConcurrentBag<OperationCompleted> completed) : IOperationObserver
        {
            public void Complete(OperationCompleted operation) => completed.Add(operation);
        }
    }
}
