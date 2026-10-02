using System.Diagnostics;
using OnuObservability.Hosting.Background;
using OnuObservability.Operations;

namespace OnuObservability.Sample.Worker;

public sealed class Worker(SampleWorkload workload) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var scenarios = new[]
        {
            SampleWorkerScenario.Success,
            SampleWorkerScenario.Partial,
            SampleWorkerScenario.DependencyOutage,
            SampleWorkerScenario.Deadline,
        };

        var index = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await workload.RunAsync(scenarios[index++ % scenarios.Length], stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (BackgroundOperationException)
            {
                // The runner records the bounded failure. A real service applies its retry policy here.
            }
            catch (OperationCanceledException)
            {
                // The deadline path is already classified and recorded by the runner.
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}

public enum SampleWorkerScenario
{
    Success,
    Partial,
    DependencyOutage,
    Deadline,
    GracefulStop,
}

public sealed class SampleWorkload(IBackgroundOperationRunner runner)
{
    public ValueTask<BackgroundOperationResult> RunAsync(
        SampleWorkerScenario scenario,
        CancellationToken hostStoppingToken)
    {
        var deadline = scenario == SampleWorkerScenario.Deadline
            ? new CancellationTokenSource(TimeSpan.FromMilliseconds(10))
            : new CancellationTokenSource();
        var cancellation = new BackgroundOperationCancellation(
            hostStoppingToken: hostStoppingToken,
            deadlineToken: deadline.Token);

        return RunAndDisposeDeadlineAsync(scenario, cancellation, deadline);
    }

    private async ValueTask<BackgroundOperationResult> RunAndDisposeDeadlineAsync(
        SampleWorkerScenario scenario,
        BackgroundOperationCancellation cancellation,
        CancellationTokenSource deadline)
    {
        using (deadline)
        {
            return await runner.RunAsync(
                WorkerTelemetry.Pass,
                cancellation,
                token => ExecuteScenarioAsync(scenario, cancellation, token));
        }
    }

    private async ValueTask<BackgroundOperationResult> ExecuteScenarioAsync(
        SampleWorkerScenario scenario,
        BackgroundOperationCancellation cancellation,
        CancellationToken token)
    {
        switch (scenario)
        {
            case SampleWorkerScenario.Success:
                return BackgroundOperationResult.Success(
                    new BackgroundOperationCounts(Processed: 10, Succeeded: 10));

            case SampleWorkerScenario.Partial:
                return new BackgroundOperationResult(
                    new OperationResult(BackgroundOperationSchema.PartialOutcome),
                    new BackgroundOperationCounts(
                        Processed: 10,
                        Succeeded: 7,
                        Failed: 1,
                        Deferred: 2,
                        Retried: 1,
                        Remaining: 2));

            case SampleWorkerScenario.DependencyOutage:
                await runner.RunDependencyAsync(
                    WorkerTelemetry.Dependency,
                    cancellation,
                    _ => ValueTask.FromException<OperationResult>(
                        new BackgroundOperationException(
                            BackgroundOperationFailureKind.Dependency)));
                throw new UnreachableException();

            case SampleWorkerScenario.Deadline:
            case SampleWorkerScenario.GracefulStop:
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new UnreachableException();

            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }
    }
}
