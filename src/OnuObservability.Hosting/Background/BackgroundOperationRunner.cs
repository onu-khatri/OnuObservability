using OnuObservability.Operations;
using OnuObservability.Safety;
using OnuObservability.Schema;

namespace OnuObservability.Hosting.Background;

public sealed class BackgroundOperationRunner(
    IOperationFactory operationFactory,
    IBackgroundOperationOutcomeClassifier outcomeClassifier) : IBackgroundOperationRunner
{
    public async ValueTask<BackgroundOperationResult> RunAsync(
        OperationDescriptor descriptor,
        BackgroundOperationCancellation cancellation,
        Func<CancellationToken, ValueTask<BackgroundOperationResult>> operation)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(operation);

        using var scope = operationFactory.Start(descriptor);
        using var linked = CreateLinkedTokenSource(cancellation);
        try
        {
            var result = await operation(linked.Token).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(result);
            result.Counts.Validate();
            RecordCounts(scope, result.Counts);
            scope.Complete(result.Operation);
            return result;
        }
        catch (Exception exception)
        {
            scope.TryComplete(outcomeClassifier.Classify(exception, cancellation));
            throw;
        }
    }

    public async ValueTask<OperationResult> RunDependencyAsync(
        OperationDescriptor descriptor,
        BackgroundOperationCancellation cancellation,
        Func<CancellationToken, ValueTask<OperationResult>> operation)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(operation);

        using var scope = operationFactory.Start(descriptor);
        using var linked = CreateLinkedTokenSource(cancellation);
        try
        {
            var result = await operation(linked.Token).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(result);
            scope.Complete(result);
            return result;
        }
        catch (Exception exception)
        {
            scope.TryComplete(outcomeClassifier.Classify(exception, cancellation));
            throw;
        }
    }

    private static CancellationTokenSource CreateLinkedTokenSource(
        BackgroundOperationCancellation cancellation) =>
        CancellationTokenSource.CreateLinkedTokenSource(
            cancellation.HostStoppingToken,
            cancellation.CallerCancellationToken,
            cancellation.DeadlineToken);

    private static void RecordCounts(
        IOperationScope scope,
        BackgroundOperationCounts counts)
    {
        scope.Record(BackgroundOperationSchema.Processed, TelemetryValue.FromCount(counts.Processed));
        scope.Record(BackgroundOperationSchema.Succeeded, TelemetryValue.FromCount(counts.Succeeded));
        scope.Record(BackgroundOperationSchema.Failed, TelemetryValue.FromCount(counts.Failed));
        scope.Record(BackgroundOperationSchema.Deferred, TelemetryValue.FromCount(counts.Deferred));
        scope.Record(BackgroundOperationSchema.Retried, TelemetryValue.FromCount(counts.Retried));
        scope.Record(BackgroundOperationSchema.Remaining, TelemetryValue.FromCount(counts.Remaining));
    }
}
