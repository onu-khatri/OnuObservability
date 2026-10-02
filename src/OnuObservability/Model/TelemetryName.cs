namespace OnuObservability.Model;

internal static class TelemetryName
{
    private const int MaximumLength = 128;

    public static string Validate(string? value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        if (value.Length > MaximumLength ||
            !IsAsciiLower(value[0]) ||
            value[^1] is '.' or '-' or '_' ||
            value.Any(character =>
                !IsAsciiLower(character) &&
                !char.IsAsciiDigit(character) &&
                character is not ('.' or '-' or '_')))
        {
            throw new ArgumentException(
                "Telemetry names must be 1-128 characters, start with a lower-case ASCII letter, and contain only lower-case ASCII letters, digits, '.', '-' or '_'.",
                parameterName);
        }

        return value;
    }

    private static bool IsAsciiLower(char value) => value is >= 'a' and <= 'z';
}
