using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using OnuObservability.Model;

namespace OnuObservability.Schema;

public sealed class TelemetrySchemaBuilder
{
    private readonly Dictionary<string, RegisteredDescriptor<OperationDescriptor>> _operations =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, RegisteredDescriptor<EventDescriptor>> _events =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, Outcome> _outcomes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ErrorCode> _errors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReasonCode> _reasons = new(StringComparer.Ordinal);
    private bool _built;

    public TelemetrySchemaBuilder(string schemaName, SchemaVersion version)
    {
        SchemaName = TelemetryName.Validate(schemaName, nameof(schemaName));
        Version = version;
    }

    public string SchemaName { get; }
    public SchemaVersion Version { get; }

    public TelemetrySchemaBuilder AddOperation(OperationDescriptor descriptor)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(descriptor);
        AddDescriptor(_operations, descriptor.Name, descriptor, DescriptorFingerprint.For);
        return this;
    }

    public TelemetrySchemaBuilder AddEvent(EventDescriptor descriptor)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(descriptor);
        AddDescriptor(_events, descriptor.Name, descriptor, DescriptorFingerprint.For);
        return this;
    }

    public TelemetrySchemaBuilder AddOutcome(Outcome outcome)
    {
        EnsureMutable();
        _outcomes.TryAdd(outcome.Value, outcome);
        return this;
    }

    public TelemetrySchemaBuilder AddError(ErrorCode error)
    {
        EnsureMutable();
        _errors.TryAdd(error.Value, error);
        return this;
    }

    public TelemetrySchemaBuilder AddReason(ReasonCode reason)
    {
        EnsureMutable();
        _reasons.TryAdd(reason.Value, reason);
        return this;
    }

    public TelemetrySchemaSnapshot Build()
    {
        EnsureMutable();
        if (_operations.Count == 0)
        {
            throw new InvalidOperationException("A telemetry schema must register at least one operation.");
        }

        if (_outcomes.Count == 0)
        {
            throw new InvalidOperationException("A telemetry schema must register at least one outcome.");
        }

        _built = true;
        return new(
            SchemaName,
            Version,
            _operations.Values.Select(item => item.Descriptor),
            _events.Values.Select(item => item.Descriptor),
            _outcomes.Values,
            _errors.Values,
            _reasons.Values);
    }

    private static void AddDescriptor<T>(
        IDictionary<string, RegisteredDescriptor<T>> target,
        string name,
        T descriptor,
        Func<T, string> fingerprint)
    {
        var candidate = new RegisteredDescriptor<T>(descriptor, fingerprint(descriptor));
        if (target.TryGetValue(name, out var existing))
        {
            if (!StringComparer.Ordinal.Equals(existing.Fingerprint, candidate.Fingerprint))
            {
                throw new InvalidOperationException($"Descriptor '{name}' was registered with conflicting definitions.");
            }

            return;
        }

        target.Add(name, candidate);
    }

    private void EnsureMutable()
    {
        if (_built)
        {
            throw new InvalidOperationException("A built telemetry schema is immutable.");
        }
    }

    private sealed record RegisteredDescriptor<T>(T Descriptor, string Fingerprint);
}

public sealed class TelemetrySchemaSnapshot
{
    internal TelemetrySchemaSnapshot(
        string name,
        SchemaVersion version,
        IEnumerable<OperationDescriptor> operations,
        IEnumerable<EventDescriptor> events,
        IEnumerable<Outcome> outcomes,
        IEnumerable<ErrorCode> errors,
        IEnumerable<ReasonCode> reasons)
    {
        Name = name;
        Version = version;
        Operations = operations.OrderBy(value => value.Name, StringComparer.Ordinal).ToImmutableArray();
        Events = events.OrderBy(value => value.Name, StringComparer.Ordinal).ToImmutableArray();
        Outcomes = outcomes.OrderBy(value => value.Value, StringComparer.Ordinal).ToImmutableArray();
        Errors = errors.OrderBy(value => value.Value, StringComparer.Ordinal).ToImmutableArray();
        Reasons = reasons.OrderBy(value => value.Value, StringComparer.Ordinal).ToImmutableArray();
    }

    public string Name { get; }
    public SchemaVersion Version { get; }
    public ImmutableArray<OperationDescriptor> Operations { get; }
    public ImmutableArray<EventDescriptor> Events { get; }
    public ImmutableArray<Outcome> Outcomes { get; }
    public ImmutableArray<ErrorCode> Errors { get; }
    public ImmutableArray<ReasonCode> Reasons { get; }
}

internal static class DescriptorFingerprint
{
    public static string For(OperationDescriptor descriptor) =>
        $"operation|{descriptor.Name}|{Fields(descriptor.Fields)}";

    public static string For(EventDescriptor descriptor) =>
        $"event|{descriptor.Id.ToString(CultureInfo.InvariantCulture)}|{descriptor.Name}|{descriptor.MessageTemplate}|{Fields(descriptor.Fields)}";

    private static string Fields(IEnumerable<TelemetryFieldDescriptor> fields)
    {
        var builder = new StringBuilder();
        foreach (var field in fields.OrderBy(value => value.Name, StringComparer.Ordinal))
        {
            builder.Append(field.Name).Append('|')
                .Append(field.Kind).Append('|')
                .Append(field.IsRequired).Append('|')
                .Append(field.MaximumLength).Append('|')
                .Append(field.MinimumInt64).Append('|')
                .Append(field.MaximumInt64).Append('|')
                .Append(field.MinimumDouble?.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(field.MaximumDouble?.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(field.Unit).Append('|')
                .AppendJoin(',', field.AllowedValues).Append('|')
                .Append(field.FallbackValue).Append(';');
        }

        return builder.ToString();
    }
}
