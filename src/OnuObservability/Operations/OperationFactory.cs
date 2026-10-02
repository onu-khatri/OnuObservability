using System.Collections.Immutable;
using OnuObservability.Model;
using OnuObservability.Safety;
using OnuObservability.Schema;

namespace OnuObservability.Operations;

public sealed class OperationFactory : IOperationFactory
{
    private readonly AsyncLocal<OperationContext?> _current = new();
    private readonly IOperationSink _sink;
    private readonly TimeProvider _timeProvider;

    public OperationFactory(IOperationSink sink, TimeProvider? timeProvider = null)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IOperationScope Start(OperationDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var parent = _current.Value;
        var context = new OperationContext(descriptor, parent);
        var scope = new OperationScope(this, context, _sink, _timeProvider);
        _current.Value = context;
        return scope;
    }

    private void Restore(OperationContext completed)
    {
        if (ReferenceEquals(_current.Value, completed))
        {
            _current.Value = completed.Parent;
        }
    }

    private sealed record OperationContext(
        OperationDescriptor Descriptor,
        OperationContext? Parent);

    private sealed class OperationScope : IOperationScope
    {
        private readonly OperationFactory _owner;
        private readonly OperationContext _context;
        private readonly IOperationObserver _observer;
        private readonly TimeProvider _timeProvider;
        private readonly DateTimeOffset _startedAtUtc;
        private readonly long _startedTimestamp;
        private readonly Dictionary<string, NormalizedTelemetryValue> _values =
            new(StringComparer.Ordinal);
        private int _completed;

        public OperationScope(
            OperationFactory owner,
            OperationContext context,
            IOperationSink sink,
            TimeProvider timeProvider)
        {
            _owner = owner;
            _context = context;
            _timeProvider = timeProvider;
            _startedAtUtc = timeProvider.GetUtcNow();
            _startedTimestamp = timeProvider.GetTimestamp();

            _observer = sink.Start(new(
                context.Descriptor,
                context.Parent?.Descriptor,
                _startedAtUtc));
        }

        public OperationDescriptor Descriptor => _context.Descriptor;

        public void Record(TelemetryFieldDescriptor field, TelemetryValue value)
        {
            EnsureActive();
            var registered = Descriptor.Fields.FirstOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.Name, field.Name));
            if (registered is null || registered != field)
            {
                throw new ArgumentException(
                    $"Field '{field.Name}' is not registered on operation '{Descriptor.Name}'.",
                    nameof(field));
            }

            var normalized = SafeTelemetryValueNormalizer.Normalize(registered, value);
            if (!normalized.IsAccepted)
            {
                _values.Remove(field.Name);
                return;
            }

            _values[field.Name] = normalized;
        }

        public void AddCount(TelemetryFieldDescriptor field, long amount)
        {
            EnsureActive();
            if (field.Kind != TelemetryValueKind.Counter)
            {
                throw new ArgumentException("Aggregate counts require a bounded Int64 field.", nameof(field));
            }

            var current = _values.TryGetValue(field.Name, out var existing) &&
                          existing.Value.TryGetInt64(out var currentValue)
                ? currentValue
                : 0;
            long total;
            try
            {
                total = checked(current + amount);
            }
            catch (OverflowException)
            {
                total = amount >= 0 ? long.MaxValue : long.MinValue;
            }

            Record(field, TelemetryValue.FromCount(total));
        }

        public void Complete(OperationResult result)
        {
            if (!TryComplete(result))
            {
                throw new InvalidOperationException("An operation scope can complete only once.");
            }
        }

        public bool TryComplete(OperationResult result)
        {
            ArgumentNullException.ThrowIfNull(result);
            if (Interlocked.Exchange(ref _completed, 1) != 0)
            {
                return false;
            }

            var missingRequired = Descriptor.Fields
                .Where(field => field.IsRequired && !_values.ContainsKey(field.Name))
                .Select(field => field.Name)
                .ToArray();
            if (missingRequired.Length != 0)
            {
                Interlocked.Exchange(ref _completed, 0);
                throw new InvalidOperationException(
                    $"Required fields are missing: {string.Join(", ", missingRequired)}.");
            }

            try
            {
                _observer.Complete(new(
                    Descriptor,
                    _context.Parent?.Descriptor,
                    _startedAtUtc,
                    _timeProvider.GetElapsedTime(_startedTimestamp),
                    result,
                    _values.ToImmutableDictionary(
                        pair => pair.Key,
                        pair => pair.Value.Value,
                        StringComparer.Ordinal),
                    Evidence(value => value.WasTruncated),
                    Evidence(value => value.WasClamped),
                    Evidence(value => value.UsedFallback)));
            }
            finally
            {
                _owner.Restore(_context);
            }

            return true;
        }

        public void Dispose()
        {
            TryComplete(new OperationResult(CommonOutcomes.InternalError));
        }

        private ImmutableHashSet<string> Evidence(Func<NormalizedTelemetryValue, bool> predicate) =>
            _values.Where(pair => predicate(pair.Value))
                .Select(pair => pair.Key)
                .ToImmutableHashSet(StringComparer.Ordinal);

        private void EnsureActive()
        {
            if (Volatile.Read(ref _completed) != 0)
            {
                throw new InvalidOperationException("The operation scope is already complete.");
            }
        }
    }
}
