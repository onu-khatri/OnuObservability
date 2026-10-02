using System.Collections.Concurrent;
using OnuObservability.Model;
using OnuObservability.Schema;

namespace OnuObservability.Logging;

public readonly record struct RateLimitDecision(
    bool IsAllowed,
    int SuppressedToReport,
    string? SuppressionReason = null);

public sealed class BoundedEventRateLimiter
{
    private readonly ConcurrentDictionary<RateLimitKey, WindowState> _windows = new();
    private readonly HashSet<string> _events;
    private readonly HashSet<string> _operations;
    private readonly HashSet<string> _outcomes;
    private readonly string _fallbackOutcome;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _window;
    private readonly int _burst;
    private readonly int _maximumKeys;
    private readonly object _admissionSync = new();

    public BoundedEventRateLimiter(
        TelemetrySchemaSnapshot schema,
        TimeProvider timeProvider,
        TimeSpan window,
        int burst,
        int maximumKeys)
    {
        ArgumentNullException.ThrowIfNull(schema);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(burst);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumKeys);
        _window = window;
        _burst = burst;
        _maximumKeys = maximumKeys;
        _events = schema.Events.Select(value => value.Name).ToHashSet(StringComparer.Ordinal);
        _operations = schema.Operations.Select(value => value.Name).ToHashSet(StringComparer.Ordinal);
        _outcomes = schema.Outcomes.Select(value => value.Value).ToHashSet(StringComparer.Ordinal);
        _fallbackOutcome = _outcomes.Contains(CommonOutcomes.Other.Value)
            ? CommonOutcomes.Other.Value
            : throw new ArgumentException("The schema must register the common 'other' outcome.", nameof(schema));
    }

    public int KeyCount => _windows.Count;

    public RateLimitDecision Acquire(
        EventDescriptor eventDescriptor,
        OperationDescriptor operationDescriptor,
        Outcome outcome)
    {
        ArgumentNullException.ThrowIfNull(eventDescriptor);
        ArgumentNullException.ThrowIfNull(operationDescriptor);
        if (!_events.Contains(eventDescriptor.Name) || !_operations.Contains(operationDescriptor.Name))
        {
            return new(false, 0, "unregistered_descriptor");
        }

        var boundedOutcome = _outcomes.Contains(outcome.Value)
            ? outcome.Value
            : _fallbackOutcome;
        var key = new RateLimitKey(
            eventDescriptor.Name,
            operationDescriptor.Name,
            boundedOutcome);

        if (!_windows.TryGetValue(key, out var state))
        {
            lock (_admissionSync)
            {
                if (!_windows.TryGetValue(key, out state))
                {
                    if (_windows.Count >= _maximumKeys)
                    {
                        return new(false, 0, "key_capacity");
                    }

                    state = new WindowState(_timeProvider.GetUtcNow());
                    _windows.TryAdd(key, state);
                }
            }
        }

        lock (state)
        {
            var now = _timeProvider.GetUtcNow();
            var suppressedToReport = 0;
            if (now - state.StartedAt >= _window)
            {
                suppressedToReport = state.Suppressed;
                state.StartedAt = now;
                state.Emitted = 0;
                state.Suppressed = 0;
            }

            if (state.Emitted < _burst)
            {
                state.Emitted++;
                return new(true, suppressedToReport);
            }

            state.Suppressed++;
            return new(false, 0, "rate_limit");
        }
    }

    private readonly record struct RateLimitKey(
        string EventName,
        string OperationName,
        string Outcome);

    private sealed class WindowState(DateTimeOffset startedAt)
    {
        public DateTimeOffset StartedAt { get; set; } = startedAt;
        public int Emitted { get; set; }
        public int Suppressed { get; set; }
    }
}
