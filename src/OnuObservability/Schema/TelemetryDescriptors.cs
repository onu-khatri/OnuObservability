using System.Collections.Immutable;
using OnuObservability.Model;

namespace OnuObservability.Schema;

public sealed record OperationDescriptor
{
    public OperationDescriptor(
        string name,
        IEnumerable<TelemetryFieldDescriptor>? fields = null)
    {
        Name = TelemetryName.Validate(name, nameof(name));
        Fields = DescriptorValidation.MaterializeFields(fields);
    }

    public string Name { get; }
    public ImmutableArray<TelemetryFieldDescriptor> Fields { get; }
}

public sealed record EventDescriptor
{
    public EventDescriptor(
        int id,
        string name,
        string messageTemplate,
        IEnumerable<TelemetryFieldDescriptor>? fields = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageTemplate);
        if (messageTemplate.Length > 256)
        {
            throw new ArgumentException("Event message templates cannot exceed 256 characters.", nameof(messageTemplate));
        }

        Id = id;
        Name = TelemetryName.Validate(name, nameof(name));
        MessageTemplate = messageTemplate;
        Fields = DescriptorValidation.MaterializeFields(fields);
    }

    public int Id { get; }
    public string Name { get; }
    public string MessageTemplate { get; }
    public ImmutableArray<TelemetryFieldDescriptor> Fields { get; }
}

internal static class DescriptorValidation
{
    public static ImmutableArray<TelemetryFieldDescriptor> MaterializeFields(
        IEnumerable<TelemetryFieldDescriptor>? fields)
    {
        var materialized = fields?.ToImmutableArray() ?? [];
        if (materialized.Any(field => field is null))
        {
            throw new ArgumentException("Descriptors cannot contain null fields.", nameof(fields));
        }

        var duplicate = materialized
            .GroupBy(field => field.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Duplicate field '{duplicate.Key}'.", nameof(fields));
        }

        return materialized.OrderBy(field => field.Name, StringComparer.Ordinal).ToImmutableArray();
    }
}
