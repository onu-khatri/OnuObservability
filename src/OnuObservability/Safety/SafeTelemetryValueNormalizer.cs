using OnuObservability.Schema;

namespace OnuObservability.Safety;

public static class SafeTelemetryValueNormalizer
{
    private static readonly string[] SensitiveMarkers =
    [
        "authorization:",
        "bearer ",
        "password=",
        "passwd=",
        "secret=",
        "token=",
        "api_key=",
        "apikey=",
        "cookie:",
        "set-cookie:",
        "http://",
        "https://",
        "file://",
        "ftp://",
    ];

    public static NormalizedTelemetryValue Normalize(
        TelemetryFieldDescriptor descriptor,
        TelemetryValue candidate)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return descriptor.Kind switch
        {
            TelemetryValueKind.FiniteString => NormalizeFinite(descriptor, candidate),
            TelemetryValueKind.BoundedText => NormalizeText(descriptor, candidate),
            TelemetryValueKind.Counter => NormalizeInt64(descriptor, candidate),
            TelemetryValueKind.Number => NormalizeDouble(descriptor, candidate),
            TelemetryValueKind.Boolean => NormalizeBoolean(candidate),
            _ => NormalizedTelemetryValue.Rejected,
        };
    }

    private static NormalizedTelemetryValue NormalizeFinite(
        TelemetryFieldDescriptor descriptor,
        TelemetryValue candidate)
    {
        if (!candidate.TryGetString(out var value) || string.IsNullOrWhiteSpace(value))
        {
            return Fallback(descriptor);
        }

        if (!descriptor.AllowedValues.Contains(value, StringComparer.Ordinal))
        {
            return Fallback(descriptor);
        }

        return new(true, TelemetryValue.FromText(value));
    }

    private static NormalizedTelemetryValue NormalizeText(
        TelemetryFieldDescriptor descriptor,
        TelemetryValue candidate)
    {
        if (!candidate.TryGetString(out var value) ||
            string.IsNullOrWhiteSpace(value) ||
            IsSensitive(value))
        {
            return NormalizedTelemetryValue.Rejected;
        }

        var maximumLength = descriptor.MaximumLength ?? 0;
        var truncated = value.Length > maximumLength;
        return new(
            true,
            TelemetryValue.FromText(truncated ? value[..maximumLength] : value),
            WasTruncated: truncated);
    }

    private static NormalizedTelemetryValue NormalizeInt64(
        TelemetryFieldDescriptor descriptor,
        TelemetryValue candidate)
    {
        if (!candidate.TryGetInt64(out var value) ||
            descriptor.MinimumInt64 is not long minimum ||
            descriptor.MaximumInt64 is not long maximum)
        {
            return NormalizedTelemetryValue.Rejected;
        }

        var normalized = Math.Clamp(value, minimum, maximum);
        return new(
            true,
            TelemetryValue.FromCount(normalized),
            WasClamped: normalized != value);
    }

    private static NormalizedTelemetryValue NormalizeDouble(
        TelemetryFieldDescriptor descriptor,
        TelemetryValue candidate)
    {
        if (!candidate.TryGetDouble(out var value) ||
            !double.IsFinite(value) ||
            descriptor.MinimumDouble is not double minimum ||
            descriptor.MaximumDouble is not double maximum)
        {
            return NormalizedTelemetryValue.Rejected;
        }

        var normalized = Math.Clamp(value, minimum, maximum);
        return new(
            true,
            TelemetryValue.FromNumber(normalized),
            WasClamped: normalized != value);
    }

    private static NormalizedTelemetryValue NormalizeBoolean(TelemetryValue candidate) =>
        candidate.TryGetBoolean(out var value)
            ? new(true, TelemetryValue.FromFlag(value))
            : NormalizedTelemetryValue.Rejected;

    private static NormalizedTelemetryValue Fallback(TelemetryFieldDescriptor descriptor) =>
        descriptor.FallbackValue is { } fallback
            ? new(true, TelemetryValue.FromText(fallback), UsedFallback: true)
            : NormalizedTelemetryValue.Rejected;

    private static bool IsSensitive(string value)
    {
        if (value.IndexOfAny(['\r', '\n', '\0']) >= 0 ||
            SensitiveMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return value.Contains('\\') ||
               value.StartsWith('/') ||
               value.Contains("/home/", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("/users/", StringComparison.OrdinalIgnoreCase);
    }
}
