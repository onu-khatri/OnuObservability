using OnuObservability.Model;
using OnuObservability.Operations;

namespace OnuObservability.Hosting.Background;

public sealed class BackgroundOperationOutcomeClassifier : IBackgroundOperationOutcomeClassifier
{
    public OperationResult Classify(
        Exception exception,
        BackgroundOperationCancellation cancellation)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException)
        {
            if (cancellation.HostStoppingToken.IsCancellationRequested)
            {
                return new OperationResult(
                    CommonOutcomes.HostStopped,
                    reason: BackgroundOperationSchema.HostStoppingReason);
            }

            if (cancellation.DeadlineToken.IsCancellationRequested)
            {
                return new OperationResult(
                    CommonOutcomes.DeadlineExceeded,
                    reason: BackgroundOperationSchema.DeadlineReason);
            }

            if (cancellation.CallerCancellationToken.IsCancellationRequested)
            {
                return new OperationResult(
                    CommonOutcomes.CallerCancelled,
                    reason: BackgroundOperationSchema.CallerCancellationReason);
            }
        }

        return exception is BackgroundOperationException classified
            ? classified.FailureKind switch
            {
                BackgroundOperationFailureKind.Validation =>
                    new OperationResult(
                        CommonOutcomes.ValidationError,
                        BackgroundOperationSchema.ValidationError),
                BackgroundOperationFailureKind.Dependency =>
                    new OperationResult(
                        CommonOutcomes.DependencyError,
                        BackgroundOperationSchema.DependencyError),
                _ => new OperationResult(
                    CommonOutcomes.InternalError,
                    BackgroundOperationSchema.InternalError),
            }
            : new OperationResult(
                CommonOutcomes.InternalError,
                BackgroundOperationSchema.InternalError);
    }
}
