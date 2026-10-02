using OnuObservability.Operations;
using OnuObservability.Schema;

namespace OnuObservability.Hosting.Background;

public enum BackgroundOperationFailureKind
{
    Validation,
    Dependency,
    Internal,
}

public sealed class BackgroundOperationException : Exception
{
    public BackgroundOperationException(
        BackgroundOperationFailureKind failureKind,
        Exception? innerException = null)
        : base("The background operation failed with a classified error.", innerException)
    {
        FailureKind = failureKind;
    }

    public BackgroundOperationFailureKind FailureKind { get; }
}

public readonly record struct BackgroundOperationCancellation
{
    public BackgroundOperationCancellation(
        CancellationToken hostStoppingToken = default,
        CancellationToken callerCancellationToken = default,
        CancellationToken deadlineToken = default)
    {
        HostStoppingToken = hostStoppingToken;
        CallerCancellationToken = callerCancellationToken;
        DeadlineToken = deadlineToken;
    }

    public CancellationToken HostStoppingToken { get; }

    public CancellationToken CallerCancellationToken { get; }

    public CancellationToken DeadlineToken { get; }
}

public readonly record struct BackgroundOperationCounts(
    long Processed = 0,
    long Succeeded = 0,
    long Failed = 0,
    long Deferred = 0,
    long Retried = 0,
    long Remaining = 0)
{
    internal void Validate()
    {
        if (Processed < 0 || Succeeded < 0 || Failed < 0 || Deferred < 0 ||
            Retried < 0 || Remaining < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BackgroundOperationCounts),
                "Background operation counts cannot be negative.");
        }
    }
}

public sealed record BackgroundOperationResult(
    OperationResult Operation,
    BackgroundOperationCounts Counts)
{
    public static BackgroundOperationResult Success(
        BackgroundOperationCounts counts = default) =>
        new(OperationResult.Success, counts);
}

public interface IBackgroundOperationOutcomeClassifier
{
    OperationResult Classify(Exception exception, BackgroundOperationCancellation cancellation);
}

public interface IBackgroundOperationRunner
{
    ValueTask<BackgroundOperationResult> RunAsync(
        OperationDescriptor descriptor,
        BackgroundOperationCancellation cancellation,
        Func<CancellationToken, ValueTask<BackgroundOperationResult>> operation);

    ValueTask<OperationResult> RunDependencyAsync(
        OperationDescriptor descriptor,
        BackgroundOperationCancellation cancellation,
        Func<CancellationToken, ValueTask<OperationResult>> operation);
}
