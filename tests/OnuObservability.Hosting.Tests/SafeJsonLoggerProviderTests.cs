using Microsoft.Extensions.Logging;
using OnuObservability.Hosting.Logging;
using OnuObservability.Logging;

namespace OnuObservability.Hosting.Tests;

public sealed class SafeJsonLoggerProviderTests
{
    [Fact]
    public void LocalSinkWritesOnlyApprovedCategoryEventsAndFields()
    {
        var writer = new StringWriter();
        var policies = new SafeLogPolicyRegistry();
        policies.Add(new SafeLogPolicy(
            OnuObservabilityDefaults.SafeLogCategory,
            [new SafeLogEventPolicy(
                OnuLogEvents.OperationCompletedEventId,
                ["OperationName", "Outcome", "DurationMilliseconds"])]));
        using var provider = new SafeJsonLoggerProvider(
            writer,
            maximumAttributeLength: 32,
            policies);
        var safe = provider.CreateLogger(OnuObservabilityDefaults.SafeLogCategory);
        var unapproved = provider.CreateLogger("Consumer.Unapproved");

        unapproved.LogWarning("synthetic-secret {PrivateValue}", "private-value");
        safe.Log(
            LogLevel.Warning,
            new EventId(9999, "Unapproved"),
            "synthetic-secret",
            new InvalidOperationException("private-exception"),
            static (state, _) => state);
        OnuLogEvents.OperationCompleted(safe, "sample.operation", "success", 12.5);

        var output = writer.ToString();
        Assert.Contains("sample.operation", output, StringComparison.Ordinal);
        Assert.Contains("success", output, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-secret", output, StringComparison.Ordinal);
        Assert.DoesNotContain("private-value", output, StringComparison.Ordinal);
        Assert.DoesNotContain("private-exception", output, StringComparison.Ordinal);
    }
}
