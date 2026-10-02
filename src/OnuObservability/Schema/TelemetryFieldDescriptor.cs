using System.Collections.Immutable;
using OnuObservability.Model;

namespace OnuObservability.Schema;

public enum TelemetryValueKind
{
    FiniteString,
    BoundedText,
    Counter,
    Number,
    Boolean,
}

public sealed record TelemetryFieldDescriptor
{
    private TelemetryFieldDescriptor(
        string name,
        TelemetryValueKind kind,
        bool isRequired,
        int? maximumLength,
        long? minimumInt64,
        long? maximumInt64,
        double? minimumDouble,
        double? maximumDouble,
        string? unit,
        ImmutableArray<string> allowedValues,
        string? fallbackValue)
    {
        Name = TelemetryName.Validate(name, nameof(name));
        Kind = kind;
        IsRequired = isRequired;
        MaximumLength = maximumLength;
        MinimumInt64 = minimumInt64;
        MaximumInt64 = maximumInt64;
        MinimumDouble = minimumDouble;
        MaximumDouble = maximumDouble;
        Unit = unit;
        AllowedValues = allowedValues;
        FallbackValue = fallbackValue;
    }

    public string Name { get; }
    public TelemetryValueKind Kind { get; }
    public bool IsRequired { get; }
    public int? MaximumLength { get; }
    public long? MinimumInt64 { get; }
    public long? MaximumInt64 { get; }
    public double? MinimumDouble { get; }
    public double? MaximumDouble { get; }
    public string? Unit { get; }
    public ImmutableArray<string> AllowedValues { get; }
    public string? FallbackValue { get; }

    public static TelemetryFieldDescriptor FiniteString(
        string name,
        IEnumerable<string> allowedValues,
        string fallbackValue,
        bool isRequired = false,
        int maximumLength = 64)
    {
        ArgumentNullException.ThrowIfNull(allowedValues);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLength, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumLength, 256);

        var values = allowedValues
            .Select(value => TelemetryName.Validate(value, nameof(allowedValues)))
            .ToImmutableArray();
        if (values.Length == 0)
        {
            throw new ArgumentException("A finite field must contain at least one value.", nameof(allowedValues));
        }

        if (values.Distinct(StringComparer.Ordinal).Count() != values.Length)
        {
            throw new ArgumentException("Finite field values must be unique.", nameof(allowedValues));
        }

        var fallback = TelemetryName.Validate(fallbackValue, nameof(fallbackValue));
        if (!values.Contains(fallback, StringComparer.Ordinal))
        {
            throw new ArgumentException("The fallback must be included in the finite values.", nameof(fallbackValue));
        }

        if (values.Any(value => value.Length > maximumLength))
        {
            throw new ArgumentException("A finite value exceeds the configured maximum length.", nameof(allowedValues));
        }

        return new(
            name,
            TelemetryValueKind.FiniteString,
            isRequired,
            maximumLength,
            null,
            null,
            null,
            null,
            null,
            values.Order(StringComparer.Ordinal).ToImmutableArray(),
            fallback);
    }

    public static TelemetryFieldDescriptor BoundedText(
        string name,
        int maximumLength,
        bool isRequired = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLength, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumLength, 256);
        return new(
            name,
            TelemetryValueKind.BoundedText,
            isRequired,
            maximumLength,
            null,
            null,
            null,
            null,
            null,
            [],
            null);
    }

    public static TelemetryFieldDescriptor Count(
        string name,
        long maximum,
        bool isRequired = false,
        long minimum = 0,
        string unit = "{item}")
    {
        if (maximum < minimum)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum), "The maximum must be at least the minimum.");
        }

        ValidateUnit(unit);

        return new(
            name,
            TelemetryValueKind.Counter,
            isRequired,
            null,
            minimum,
            maximum,
            null,
            null,
            unit,
            [],
            null);
    }

    public static TelemetryFieldDescriptor Number(
        string name,
        double minimum,
        double maximum,
        bool isRequired = false,
        string unit = "1")
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum < minimum)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum), "Numeric fields require finite ordered bounds.");
        }

        ValidateUnit(unit);

        return new(
            name,
            TelemetryValueKind.Number,
            isRequired,
            null,
            null,
            null,
            minimum,
            maximum,
            unit,
            [],
            null);
    }

    public static TelemetryFieldDescriptor Flag(string name, bool isRequired = false) =>
        new(
            name,
            TelemetryValueKind.Boolean,
            isRequired,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            null);

    private static void ValidateUnit(string unit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unit);
        if (unit.Length > 32 || unit.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not ('{' or '}' or '/' or '.')))
        {
            throw new ArgumentException("Units must be 1-32 safe ASCII unit characters.", nameof(unit));
        }
    }
}
