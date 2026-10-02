using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using OnuObservability.Logging;
using OnuObservability.Model;
using OnuObservability.Operations;
using OnuObservability.Safety;
using OnuObservability.Schema;

namespace OnuObservability.Hosting.Telemetry;

internal sealed class OpenTelemetryOperationSink(
    OnuTelemetryInstruments instruments,
    ILoggerFactory loggerFactory,
    SafeEventRateLimiter rateLimiter,
    TelemetrySchemaSnapshot schema) : IOperationSink
{
    private readonly ILogger _logger = loggerFactory.CreateLogger(OnuObservabilityDefaults.SafeLogCategory);
    private readonly EventDescriptor _completedEvent = schema.Events.Single(value =>
        value.Name == "onu.operation.completed");
    private readonly EventDescriptor _failedEvent = schema.Events.Single(value =>
        value.Name == "onu.operation.failed");
    private readonly Dictionary<string, OperationDescriptor> _operations =
        schema.Operations.ToDictionary(value => value.Name, StringComparer.Ordinal);
    private readonly Dictionary<string, Outcome> _outcomes =
        schema.Outcomes.ToDictionary(value => value.Value, StringComparer.Ordinal);
    private readonly Dictionary<string, ErrorCode> _errors =
        schema.Errors.ToDictionary(value => value.Value, StringComparer.Ordinal);
    private readonly Dictionary<string, ReasonCode> _reasons =
        schema.Reasons.ToDictionary(value => value.Value, StringComparer.Ordinal);

    public IOperationObserver Start(OperationStarted operation)
    {
        if (!_operations.TryGetValue(operation.Descriptor.Name, out var registered) ||
            !ReferenceEquals(registered, operation.Descriptor))
        {
            throw new ArgumentException(
                $"Operation '{operation.Descriptor.Name}' is not a registered descriptor.",
                nameof(operation));
        }

        var activity = OnuTelemetrySources.Activities.StartActivity(
            operation.Descriptor.Name,
            ActivityKind.Internal);
        return new Observer(
            activity,
            instruments,
            _logger,
            rateLimiter,
            _completedEvent,
            _failedEvent,
            _outcomes,
            _errors,
            _reasons);
    }

    private sealed class Observer(
        Activity? activity,
        OnuTelemetryInstruments instruments,
        ILogger logger,
        SafeEventRateLimiter rateLimiter,
        EventDescriptor completedEvent,
        EventDescriptor failedEvent,
        Dictionary<string, Outcome> outcomes,
        Dictionary<string, ErrorCode> errors,
        Dictionary<string, ReasonCode> reasons) : IOperationObserver
    {
        private int _completed;

        public void Complete(OperationCompleted operation)
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
            {
                return;
            }

            operation = operation with { Result = Normalize(operation.Result) };
            var tags = new TagList
            {
                { "operation", operation.Descriptor.Name },
                { "outcome", operation.Result.Outcome.Value },
            };
            instruments.Operations.Add(1, tags);
            instruments.OperationDuration.Record(operation.Duration.TotalSeconds, tags);
            WriteSafeEvent(operation);

            if (activity is null)
            {
                return;
            }

            activity.SetTag("onu.operation", operation.Descriptor.Name);
            activity.SetTag("onu.outcome", operation.Result.Outcome.Value);
            if (operation.Result.Error is { } error)
            {
                activity.SetTag("error.type", error.Value);
            }

            if (operation.Result.Reason is { } reason)
            {
                activity.SetTag("onu.reason", reason.Value);
            }

            foreach (var (name, value) in operation.Fields)
            {
                SetTag(activity, name, value);
            }

            foreach (var name in operation.TruncatedFields)
            {
                activity.SetTag($"onu.evidence.{name}.truncated", true);
            }

            foreach (var name in operation.ClampedFields)
            {
                activity.SetTag($"onu.evidence.{name}.clamped", true);
            }

            foreach (var name in operation.FallbackFields)
            {
                activity.SetTag($"onu.evidence.{name}.fallback", true);
            }

            activity.SetStatus(
                operation.Result.Outcome == CommonOutcomes.Success
                    ? ActivityStatusCode.Ok
                    : ActivityStatusCode.Error);
            activity.Stop();
        }

        private OperationResult Normalize(OperationResult result)
        {
            var outcome = outcomes.TryGetValue(result.Outcome.Value, out var registeredOutcome)
                ? registeredOutcome
                : outcomes[CommonOutcomes.Other.Value];
            ErrorCode? error = null;
            if (result.Error is { } candidateError)
            {
                error = errors.TryGetValue(candidateError.Value, out var registeredError)
                    ? registeredError
                    : errors.GetValueOrDefault("other");
            }

            ReasonCode? reason = null;
            if (result.Reason is { } candidateReason)
            {
                reason = reasons.TryGetValue(candidateReason.Value, out var registeredReason)
                    ? registeredReason
                    : reasons.GetValueOrDefault("other");
            }

            return new(outcome, error, reason);
        }

        private void WriteSafeEvent(OperationCompleted operation)
        {
            var eventDescriptor = operation.Result.Outcome == CommonOutcomes.Success
                ? completedEvent
                : failedEvent;
            var decision = rateLimiter.Acquire(
                eventDescriptor,
                operation.Descriptor,
                operation.Result.Outcome);
            if (decision.SuppressedToReport > 0)
            {
                OnuLogEvents.SuppressionSummary(
                    logger,
                    decision.SuppressedToReport,
                    eventDescriptor.Name);
            }

            if (!decision.IsAllowed)
            {
                return;
            }

            if (operation.Result.Outcome == CommonOutcomes.Success)
            {
                OnuLogEvents.OperationCompleted(
                    logger,
                    operation.Descriptor.Name,
                    operation.Result.Outcome.Value,
                    operation.Duration.TotalMilliseconds);
            }
            else
            {
                OnuLogEvents.OperationFailed(
                    logger,
                    operation.Descriptor.Name,
                    operation.Result.Outcome.Value,
                    operation.Result.Error?.Value ?? "other");
            }
        }

        private static void SetTag(Activity target, string name, TelemetryValue value)
        {
            if (value.TryGetString(out var text))
            {
                target.SetTag(name, text);
            }
            else if (value.TryGetInt64(out var count))
            {
                target.SetTag(name, count);
            }
            else if (value.TryGetDouble(out var number))
            {
                target.SetTag(name, number);
            }
            else if (value.TryGetBoolean(out var flag))
            {
                target.SetTag(name, flag);
            }
        }
    }
}
