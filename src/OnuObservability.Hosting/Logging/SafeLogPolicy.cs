using System.Collections.Immutable;

namespace OnuObservability.Hosting.Logging;

public sealed record SafeLogEventPolicy
{
    public SafeLogEventPolicy(int eventId, IEnumerable<string> approvedFields)
        : this(eventId, eventId.ToString(System.Globalization.CultureInfo.InvariantCulture), approvedFields)
    {
    }

    public SafeLogEventPolicy(
        int eventId,
        string eventName,
        IEnumerable<string> approvedFields)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(approvedFields);
        if (eventName.Length > 128 || eventName.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-')))
        {
            throw new ArgumentException(
                "Safe log event names must be bounded ASCII identifiers.",
                nameof(eventName));
        }

        EventId = eventId;
        EventName = eventName;
        ApprovedFields = approvedFields
            .Select(ValidateField)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
    }

    public int EventId { get; }

    public string EventName { get; }

    public ImmutableArray<string> ApprovedFields { get; }

    private static string ValidateField(string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        if (field.Length > 64 || field.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '.')))
        {
            throw new ArgumentException("Safe log field names must be bounded ASCII identifiers.", nameof(field));
        }

        return field;
    }
}

public sealed record SafeLogPolicy
{
    public SafeLogPolicy(string category, IEnumerable<SafeLogEventPolicy> events)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentNullException.ThrowIfNull(events);
        if (category.Length > 128 || category.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-')))
        {
            throw new ArgumentException("Safe log categories must be bounded ASCII identifiers.", nameof(category));
        }

        Category = category;
        Events = events.OrderBy(value => value.EventId).ToImmutableArray();
        if (Events.Select(value => value.EventId).Distinct().Count() != Events.Length)
        {
            throw new ArgumentException("Safe log event identifiers must be unique.", nameof(events));
        }
    }

    public string Category { get; }

    public ImmutableArray<SafeLogEventPolicy> Events { get; }
}

internal sealed class SafeLogPolicyRegistry
{
    private readonly Dictionary<string, SafeLogPolicy> _policies = new(StringComparer.Ordinal);

    public void Add(SafeLogPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (_policies.TryGetValue(policy.Category, out var existing))
        {
            if (!Equivalent(existing, policy))
            {
                throw new InvalidOperationException(
                    $"Safe log category '{policy.Category}' has conflicting policies.");
            }

            return;
        }

        _policies.Add(policy.Category, policy);
    }

    private static bool Equivalent(SafeLogPolicy left, SafeLogPolicy right) =>
        StringComparer.Ordinal.Equals(left.Category, right.Category) &&
        left.Events.Length == right.Events.Length &&
        left.Events.Zip(right.Events).All(pair =>
            pair.First.EventId == pair.Second.EventId &&
            StringComparer.Ordinal.Equals(pair.First.EventName, pair.Second.EventName) &&
            pair.First.ApprovedFields.SequenceEqual(
                pair.Second.ApprovedFields,
                StringComparer.Ordinal));

    public bool IsCategoryAllowed(string? category) =>
        category is not null && _policies.ContainsKey(category);

    public bool TryGetEvent(
        string category,
        int eventId,
        out SafeLogEventPolicy? eventPolicy)
    {
        if (_policies.TryGetValue(category, out var policy) &&
            policy.Events.FirstOrDefault(value => value.EventId == eventId) is { } registered)
        {
            eventPolicy = registered;
            return true;
        }

        eventPolicy = null;
        return false;
    }
}
