using System.Collections.Concurrent;
using OnuObservability.Model;

namespace OnuObservability.Mcp;

internal sealed class McpLogRateLimiter
{
    private static readonly HashSet<string> Events =
    [
        "mcp.tool.completed",
        "mcp.tool.failed",
        McpTelemetrySchema.Other,
    ];

    private readonly ConcurrentDictionary<Key, State> _states = new();
    private readonly HashSet<string> _tools;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _window;
    private readonly int _burst;
    private readonly int _maximumKeys;
    private readonly object _admissionSync = new();

    public McpLogRateLimiter(
        McpRegisteredToolRegistry registry,
        McpObservabilityOptions options,
        TimeProvider timeProvider)
    {
        _tools = registry.Names.ToHashSet(StringComparer.Ordinal);
        _tools.Add(McpTelemetrySchema.Unknown);
        _timeProvider = timeProvider;
        _window = TimeSpan.FromSeconds(options.LogRateLimitWindowSeconds);
        _burst = options.LogRateLimitBurst;
        _maximumKeys = options.MaximumLogRateLimitKeys;
    }

    public Decision Acquire(string eventName, string toolName, Outcome outcome)
    {
        eventName = Events.Contains(eventName) ? eventName : McpTelemetrySchema.Other;
        var boundedTool = _tools.Contains(toolName) ? toolName : McpTelemetrySchema.Unknown;
        var boundedOutcome = McpTelemetryValues.NormalizeOutcome(outcome);
        var key = new Key(eventName, boundedTool, boundedOutcome.Value);
        if (!_states.TryGetValue(key, out var state))
        {
            lock (_admissionSync)
            {
                if (!_states.TryGetValue(key, out state))
                {
                    if (_states.Count >= _maximumKeys)
                    {
                        return new(false, 0);
                    }

                    state = new State(_timeProvider.GetUtcNow());
                    _states.TryAdd(key, state);
                }
            }
        }

        lock (state)
        {
            var now = _timeProvider.GetUtcNow();
            var suppressed = 0;
            if (now - state.Started >= _window)
            {
                suppressed = state.Suppressed;
                state.Started = now;
                state.Emitted = 0;
                state.Suppressed = 0;
            }

            if (state.Emitted++ < _burst)
            {
                return new(true, suppressed);
            }

            state.Suppressed++;
            return new(false, 0);
        }
    }

    public readonly record struct Decision(bool Allowed, int Suppressed);

    internal int KeyCount => _states.Count;

    private readonly record struct Key(string Event, string Tool, string Outcome);

    private sealed class State(DateTimeOffset started)
    {
        public DateTimeOffset Started { get; set; } = started;
        public int Emitted { get; set; }
        public int Suppressed { get; set; }
    }
}
