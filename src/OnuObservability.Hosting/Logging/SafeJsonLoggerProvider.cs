using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace OnuObservability.Hosting.Logging;

internal sealed class SafeJsonLoggerProvider(
    TextWriter writer,
    int maximumAttributeLength,
    SafeLogPolicyRegistry policies) : ILoggerProvider
{
    private readonly object _gate = new();

    public ILogger CreateLogger(string categoryName) => new SafeJsonLogger(
        categoryName,
        writer,
        maximumAttributeLength,
        _gate,
        policies);

    public void Dispose()
    {
    }

    private sealed class SafeJsonLogger(
        string category,
        TextWriter writer,
        int maximumAttributeLength,
        object gate,
        SafeLogPolicyRegistry policies) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            policies.IsCategoryAllowed(category) &&
            logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) ||
                !policies.TryGetEvent(category, eventId.Id, out var eventPolicy))
            {
                return;
            }

            var approvedFields = eventPolicy!.ApprovedFields;
            var fields = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                foreach (var (key, value) in values)
                {
                    if (approvedFields.Contains(key))
                    {
                        fields[key] = Normalize(value);
                    }
                }
            }

            var activity = Activity.Current;
            var record = JsonSerializer.Serialize(new
            {
                Timestamp = DateTime.UtcNow.ToString(
                    "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                    System.Globalization.CultureInfo.InvariantCulture),
                EventId = eventId.Id,
                EventName = eventPolicy.EventName,
                LogLevel = logLevel.ToString(),
                Category = category,
                TraceId = activity?.TraceId.ToHexString(),
                SpanId = activity?.SpanId.ToHexString(),
                TraceFlags = activity?.ActivityTraceFlags.ToString(),
                Fields = fields,
            });
            lock (gate)
            {
                writer.WriteLine(record);
            }
        }

        private object? Normalize(object? value) => value switch
        {
            null => null,
            string text when text.Length > maximumAttributeLength =>
                text[..maximumAttributeLength],
            string text => text,
            bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => value,
            _ => null,
        };
    }
}
