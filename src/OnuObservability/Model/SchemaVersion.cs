using System.Globalization;

namespace OnuObservability.Model;

public readonly record struct SchemaVersion
{
    public SchemaVersion(int major, int minor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        Major = major;
        Minor = minor;
    }

    public int Major { get; }
    public int Minor { get; }

    public static SchemaVersion Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var parts = value.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            major < 0 || minor < 0)
        {
            throw new FormatException("Schema versions must use the non-negative 'major.minor' form.");
        }

        return new(major, minor);
    }

    public override string ToString() => $"{Major}.{Minor}";
}
