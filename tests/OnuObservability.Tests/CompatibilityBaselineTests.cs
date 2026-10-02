using System.Globalization;
using System.Reflection;
using OnuObservability.Model;
using OnuObservability.Schema;

namespace OnuObservability.Tests;

public sealed class CompatibilityBaselineTests
{
    [Fact]
    public void PublicApiMatchesCheckedInBaseline()
    {
        var expected = ReadBaseline("core-public-api.txt");
        var actual = PublicApiManifest.Create(typeof(Outcome).Assembly);

        Assert.True(
            expected.SequenceEqual(actual, StringComparer.Ordinal),
            $"Public API baseline mismatch.{Environment.NewLine}{string.Join(Environment.NewLine, actual)}");
    }

    [Fact]
    public void CoreSchemaMatchesCheckedInBaseline()
    {
        var expected = ReadBaseline("core-schema.txt");
        var actual = SchemaManifest.Create(CoreTelemetrySchema.Create());

        Assert.True(
            expected.SequenceEqual(actual, StringComparer.Ordinal),
            $"Schema baseline mismatch.{Environment.NewLine}{string.Join(Environment.NewLine, actual)}");
    }

    [Fact]
    public void PublicApiCompatibilityDetectsRemovalAndSignatureChange()
    {
        var baseline = PublicApiManifest.Create(typeof(Outcome).Assembly);
        var removed = baseline.Skip(1).ToArray();
        var changed = baseline
            .Select(line => line.Contains("SchemaVersion|", StringComparison.Ordinal)
                ? line.Replace("ctor(System.Int32,System.Int32)", "ctor(System.Int64,System.Int64)", StringComparison.Ordinal)
                : line)
            .ToArray();

        Assert.NotEmpty(CompatibilityDiff.RemovedLines(baseline, removed));
        Assert.NotEmpty(CompatibilityDiff.RemovedLines(baseline, changed));
    }

    [Fact]
    public void SchemaCompatibilityDetectsRenameUnitAndBoundedMeaningChanges()
    {
        var baseline = SchemaManifest.Create(CoreTelemetrySchema.Create());
        var renamed = baseline
            .Select(line => line.Replace("onu.pipeline.export", "onu.pipeline.export_renamed", StringComparison.Ordinal))
            .ToArray();
        var unitChanged = baseline
            .Select(line => line.StartsWith("field|operation:onu.pipeline.export|count|", StringComparison.Ordinal)
                ? line.Replace("|{item}|", "|ms|", StringComparison.Ordinal)
                : line)
            .ToArray();
        var meaningChanged = baseline
            .Select(line => line.StartsWith("field|operation:onu.pipeline.export|count|", StringComparison.Ordinal)
                ? line.Replace("|1000000|", "|999|", StringComparison.Ordinal)
                : line)
            .ToArray();

        Assert.Contains(
            CompatibilityDiff.CompareSchema(baseline, renamed),
            value => value.StartsWith("removed-signal:", StringComparison.Ordinal));
        Assert.Contains(
            CompatibilityDiff.CompareSchema(baseline, unitChanged),
            value => value.StartsWith("changed-unit:", StringComparison.Ordinal));
        Assert.Contains(
            CompatibilityDiff.CompareSchema(baseline, meaningChanged),
            value => value.StartsWith("changed-meaning:", StringComparison.Ordinal));
    }

    private static string[] ReadBaseline(string name) =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Baselines", name))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
}

internal static class PublicApiManifest
{
    private static readonly HashSet<string> IgnoredMethodNames = new(StringComparer.Ordinal)
    {
        "Equals",
        "GetHashCode",
        "ToString",
        "Deconstruct",
        "<Clone>$",
    };

    public static string[] Create(Assembly assembly) => assembly
        .GetExportedTypes()
        .OrderBy(type => type.FullName, StringComparer.Ordinal)
        .Select(CreateTypeLine)
        .ToArray();

    private static string CreateTypeLine(Type type)
    {
        var members = new List<string>();
        members.AddRange(type
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(constructor => $"ctor({Parameters(constructor.GetParameters())})"));
        members.AddRange(type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(property => $"property {TypeName(property.PropertyType)} {property.Name}"));
        members.AddRange(type
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field.Name != "value__")
            .Select(field => $"field {TypeName(field.FieldType)} {field.Name}"));
        members.AddRange(type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName && !IgnoredMethodNames.Contains(method.Name))
            .Select(method =>
                $"method {TypeName(method.ReturnType)} {method.Name}({Parameters(method.GetParameters())})"));

        return $"{type.FullName}|{string.Join(';', members.Order(StringComparer.Ordinal))}";
    }

    private static string Parameters(IEnumerable<ParameterInfo> parameters) =>
        string.Join(',', parameters.Select(parameter => TypeName(parameter.ParameterType)));

    private static string TypeName(Type type)
    {
        if (type.IsByRef)
        {
            return $"{TypeName(type.GetElementType()!)}&";
        }

        if (type.IsArray)
        {
            return $"{TypeName(type.GetElementType()!)}[]";
        }

        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        var definitionName = type.GetGenericTypeDefinition().FullName!;
        definitionName = definitionName[..definitionName.LastIndexOf('`')];
        return $"{definitionName}<{string.Join(',', type.GetGenericArguments().Select(TypeName))}>";
    }
}

internal static class SchemaManifest
{
    public static string[] Create(TelemetrySchemaSnapshot schema)
    {
        var lines = new List<string> { $"schema|{schema.Name}|{schema.Version}" };
        lines.AddRange(schema.Errors.Select(value => $"error|{value.Value}"));
        foreach (var eventDescriptor in schema.Events)
        {
            lines.Add($"event|{eventDescriptor.Name}|{eventDescriptor.Id.ToString(CultureInfo.InvariantCulture)}|{eventDescriptor.MessageTemplate}");
            lines.AddRange(eventDescriptor.Fields.Select(field => Field($"event:{eventDescriptor.Name}", field)));
        }

        foreach (var operation in schema.Operations)
        {
            lines.Add($"operation|{operation.Name}");
            lines.AddRange(operation.Fields.Select(field => Field($"operation:{operation.Name}", field)));
        }

        lines.AddRange(schema.Outcomes.Select(value => $"outcome|{value.Value}"));
        lines.AddRange(schema.Reasons.Select(value => $"reason|{value.Value}"));
        return lines.ToArray();
    }

    private static string Field(string owner, TelemetryFieldDescriptor field) => string.Join('|',
    [
        "field",
        owner,
        field.Name,
        field.Kind.ToString(),
        field.IsRequired.ToString(CultureInfo.InvariantCulture),
        Invariant(field.MaximumLength),
        Invariant(field.MinimumInt64),
        Invariant(field.MaximumInt64),
        Invariant(field.MinimumDouble),
        Invariant(field.MaximumDouble),
        field.Unit ?? string.Empty,
        string.Join(',', field.AllowedValues),
        field.FallbackValue ?? string.Empty,
    ]);

    private static string Invariant<T>(T? value)
        where T : struct, IFormattable =>
        value?.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;
}

internal static class CompatibilityDiff
{
    public static string[] RemovedLines(IEnumerable<string> baseline, IEnumerable<string> candidate)
    {
        var candidateSet = candidate.ToHashSet(StringComparer.Ordinal);
        return baseline.Where(line => !candidateSet.Contains(line)).ToArray();
    }

    public static string[] CompareSchema(IEnumerable<string> baseline, IEnumerable<string> candidate)
    {
        var baselineArray = baseline.ToArray();
        var candidateArray = candidate.ToArray();
        var differences = new List<string>();
        var candidateSignals = candidateArray
            .Where(IsSignal)
            .ToHashSet(StringComparer.Ordinal);
        differences.AddRange(baselineArray
            .Where(IsSignal)
            .Where(line => !candidateSignals.Contains(line))
            .Select(line => $"removed-signal:{line}"));

        var baselineFields = FieldsByKey(baselineArray);
        var candidateFields = FieldsByKey(candidateArray);
        foreach (var (key, baselineParts) in baselineFields)
        {
            if (!candidateFields.TryGetValue(key, out var candidateParts))
            {
                continue;
            }

            if (!StringComparer.Ordinal.Equals(baselineParts[10], candidateParts[10]))
            {
                differences.Add($"changed-unit:{key}");
            }

            if (!baselineParts.Where((_, index) => index != 10)
                    .SequenceEqual(candidateParts.Where((_, index) => index != 10), StringComparer.Ordinal))
            {
                differences.Add($"changed-meaning:{key}");
            }
        }

        return differences.ToArray();
    }

    private static bool IsSignal(string line) =>
        line.StartsWith("operation|", StringComparison.Ordinal) ||
        line.StartsWith("event|", StringComparison.Ordinal);

    private static Dictionary<string, string[]> FieldsByKey(IEnumerable<string> lines) => lines
        .Where(line => line.StartsWith("field|", StringComparison.Ordinal))
        .Select(line => line.Split('|'))
        .ToDictionary(parts => $"{parts[1]}|{parts[2]}", StringComparer.Ordinal);
}
