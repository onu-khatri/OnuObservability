using System.Collections.Immutable;

namespace OnuObservability.Model;

public readonly record struct Outcome
{
    public Outcome(string value) => Value = TelemetryName.Validate(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct ErrorCode
{
    public ErrorCode(string value) => Value = TelemetryName.Validate(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct ReasonCode
{
    public ReasonCode(string value) => Value = TelemetryName.Validate(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed class FiniteValueRegistry<T>
    where T : notnull
{
    private readonly ImmutableDictionary<string, T> _values;
    private readonly Func<T, string> _getName;

    public FiniteValueRegistry(
        IEnumerable<T> values,
        T fallback,
        Func<T, string> getName)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(getName);

        _getName = getName;
        var builder = ImmutableDictionary.CreateBuilder<string, T>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var name = TelemetryName.Validate(getName(value), nameof(values));
            if (!builder.TryAdd(name, value))
            {
                throw new ArgumentException($"Duplicate registered value '{name}'.", nameof(values));
            }
        }

        if (builder.Count == 0)
        {
            throw new ArgumentException("A finite registry cannot be empty.", nameof(values));
        }

        FallbackName = TelemetryName.Validate(getName(fallback), nameof(fallback));
        if (!builder.ContainsKey(FallbackName))
        {
            throw new ArgumentException("The fallback must be included in the registry.", nameof(fallback));
        }

        _values = builder.ToImmutable();
        Values = _values.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .ToImmutableArray();
    }

    public ImmutableArray<T> Values { get; }

    public string FallbackName { get; }

    public T Normalize(T value) =>
        _values.TryGetValue(_getName(value), out var registered)
            ? registered
            : _values[FallbackName];

    public bool Contains(T value) => _values.ContainsKey(_getName(value));
}

public static class CommonOutcomes
{
    public static Outcome Success { get; } = new("success");
    public static Outcome ValidationError { get; } = new("validation_error");
    public static Outcome AuthorizationDenied { get; } = new("authorization_denied");
    public static Outcome CallerCancelled { get; } = new("caller_cancelled");
    public static Outcome HostStopped { get; } = new("host_stopped");
    public static Outcome DeadlineExceeded { get; } = new("deadline_exceeded");
    public static Outcome DependencyError { get; } = new("dependency_error");
    public static Outcome InternalError { get; } = new("internal_error");
    public static Outcome Other { get; } = new("other");

    public static FiniteValueRegistry<Outcome> Registry { get; } = new(
        [
            Success,
            ValidationError,
            AuthorizationDenied,
            CallerCancelled,
            HostStopped,
            DeadlineExceeded,
            DependencyError,
            InternalError,
            Other,
        ],
        Other,
        outcome => outcome.Value);
}
