using OnuObservability.Model;
using OnuObservability.Mcp.Classification;
using OnuObservability.Mcp.Schema;

namespace OnuObservability.Sample.Mcp;

/// <summary>
/// A domain exception. Its message is never recorded in telemetry; only the bounded
/// outcome/error/reason produced by <see cref="SampleOutcomeClassifier"/> is emitted.
/// </summary>
public sealed class AccountSuspendedException : Exception
{
    public AccountSuspendedException() : base("The account is suspended.") { }
}

/// <summary>
/// A custom <see cref="IMcpTelemetryOutcomeClassifier"/>. Register it before
/// <c>WithOnuObservability</c> so the adapter's <c>TryAddSingleton</c> keeps the
/// domain-aware classifier. Unknown exceptions are delegated to the built-in classifier.
/// </summary>
public sealed class SampleOutcomeClassifier : IMcpTelemetryOutcomeClassifier
{
    private readonly McpTelemetryOutcomeClassifier _inner = new();

    public McpTelemetryClassification Classify(
        Exception exception,
        CancellationToken callerCancellation = default)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            AccountSuspendedException => new McpTelemetryClassification(
                McpTelemetrySchema.ToolErrorOutcome,
                new ErrorCode("policy_denied"),
                DenialReason: McpDenialReason.PolicyDenied),
            _ => _inner.Classify(exception, callerCancellation),
        };
    }
}
