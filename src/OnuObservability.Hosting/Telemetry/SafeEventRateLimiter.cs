using OnuObservability.Hosting.Configuration;
using OnuObservability.Logging;
using OnuObservability.Model;
using OnuObservability.Schema;

namespace OnuObservability.Hosting.Telemetry;

internal sealed class SafeEventRateLimiter
{
    private readonly BoundedEventRateLimiter _inner;

    public SafeEventRateLimiter(
        TelemetrySchemaSnapshot schema,
        TimeProvider timeProvider,
        EffectiveObservabilitySettings settings)
    {
        _inner = new(
            schema,
            timeProvider,
            settings.LogRateLimitWindow,
            settings.LogRateLimitBurst,
            settings.MaximumLogRateLimitKeys);
    }

    public RateLimitDecision Acquire(
        EventDescriptor eventDescriptor,
        OperationDescriptor operationDescriptor,
        Outcome outcome) =>
        _inner.Acquire(eventDescriptor, operationDescriptor, outcome);
}
