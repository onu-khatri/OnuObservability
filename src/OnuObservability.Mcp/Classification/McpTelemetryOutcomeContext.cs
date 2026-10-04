namespace OnuObservability.Mcp.Classification;

public interface IMcpTelemetryOutcomeContext
{
    IMcpTelemetryOutcomeScope BeginScope();

    void Record(McpTelemetryClassification classification);
}

public interface IMcpTelemetryOutcomeScope : IDisposable
{
    McpTelemetryClassification? Classification { get; }
}

public sealed class McpTelemetryOutcomeContext : IMcpTelemetryOutcomeContext
{
    private readonly AsyncLocal<ScopeState?> _current = new();

    public IMcpTelemetryOutcomeScope BeginScope()
    {
        var state = new ScopeState(_current.Value);
        _current.Value = state;
        return new Scope(this, state);
    }

    public void Record(McpTelemetryClassification classification)
    {
        ArgumentNullException.ThrowIfNull(classification);
        _current.Value?.Record(classification);
    }

    private sealed class Scope(McpTelemetryOutcomeContext owner, ScopeState state)
        : IMcpTelemetryOutcomeScope
    {
        private ScopeState? _state = state;

        public McpTelemetryClassification? Classification =>
            Volatile.Read(ref _state)?.Classification;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _state, null);
            if (current is null)
            {
                return;
            }

            if (!ReferenceEquals(owner._current.Value, current))
            {
                throw new InvalidOperationException(
                    "MCP telemetry outcome scopes must be disposed in reverse creation order.");
            }

            owner._current.Value = current.Parent;
        }
    }

    private sealed class ScopeState(ScopeState? parent)
    {
        private McpTelemetryClassification? _classification;

        public ScopeState? Parent { get; } = parent;

        public McpTelemetryClassification? Classification =>
            Volatile.Read(ref _classification);

        public void Record(McpTelemetryClassification classification) =>
            Interlocked.CompareExchange(ref _classification, classification, null);
    }
}
