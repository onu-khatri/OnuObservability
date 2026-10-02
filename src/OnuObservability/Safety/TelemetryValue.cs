using System.Globalization;
using OnuObservability.Schema;

namespace OnuObservability.Safety;

public readonly struct TelemetryValue
{
    private readonly string? _stringValue;
    private readonly long _int64Value;
    private readonly double _doubleValue;
    private readonly bool _booleanValue;

    private TelemetryValue(TelemetryValueKind kind, string? stringValue = null)
    {
        Kind = kind;
        _stringValue = stringValue;
    }

    private TelemetryValue(long value)
    {
        Kind = TelemetryValueKind.Counter;
        _int64Value = value;
    }

    private TelemetryValue(double value)
    {
        Kind = TelemetryValueKind.Number;
        _doubleValue = value;
    }

    private TelemetryValue(bool value)
    {
        Kind = TelemetryValueKind.Boolean;
        _booleanValue = value;
    }

    public TelemetryValueKind Kind { get; }

    public static TelemetryValue FromText(string? value) =>
        new(TelemetryValueKind.BoundedText, value);

    public static TelemetryValue FromCount(long value) => new(value);

    public static TelemetryValue FromNumber(double value) => new(value);

    public static TelemetryValue FromFlag(bool value) => new(value);

    public bool TryGetString(out string? value)
    {
        value = _stringValue;
        return Kind is TelemetryValueKind.BoundedText or TelemetryValueKind.FiniteString;
    }

    public bool TryGetInt64(out long value)
    {
        value = _int64Value;
        return Kind == TelemetryValueKind.Counter;
    }

    public bool TryGetDouble(out double value)
    {
        value = _doubleValue;
        return Kind == TelemetryValueKind.Number;
    }

    public bool TryGetBoolean(out bool value)
    {
        value = _booleanValue;
        return Kind == TelemetryValueKind.Boolean;
    }

    public string ToInvariantString() => Kind switch
    {
        TelemetryValueKind.FiniteString or TelemetryValueKind.BoundedText => _stringValue ?? string.Empty,
        TelemetryValueKind.Counter => _int64Value.ToString(CultureInfo.InvariantCulture),
        TelemetryValueKind.Number => _doubleValue.ToString("R", CultureInfo.InvariantCulture),
        TelemetryValueKind.Boolean => _booleanValue ? "true" : "false",
        _ => string.Empty,
    };
}

public readonly record struct NormalizedTelemetryValue(
    bool IsAccepted,
    TelemetryValue Value,
    bool WasTruncated = false,
    bool WasClamped = false,
    bool UsedFallback = false)
{
    public static NormalizedTelemetryValue Rejected { get; } = new(false, default);
}
