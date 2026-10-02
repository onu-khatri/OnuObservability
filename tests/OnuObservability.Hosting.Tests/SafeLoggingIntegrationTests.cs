using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OnuObservability.Operations;
using OnuObservability.Schema;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;

namespace OnuObservability.Hosting.Tests;

public sealed class SafeLoggingIntegrationTests
{
    [Fact]
    public void RegistrationPreservesExistingLoggerAndExportsOnlyApprovedCategory()
    {
        var existing = new RecordingLoggerProvider();
        var exported = new List<LogRecord>();
        var activities = new List<Activity>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OnuObservability:ServiceName"] = "sample-service",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(existing));
        services.AddOnuObservability(configuration).AddOperation(new("sample.operation"));
        services.ConfigureOpenTelemetryTracerProvider(tracing =>
            tracing.AddInMemoryExporter(activities));
        services.AddLogging(logging => logging.AddOpenTelemetry(options =>
            options.AddInMemoryExporter(exported)));

        using var provider = services.BuildServiceProvider();
        var tracerProvider = provider.GetRequiredService<TracerProvider>();
        provider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Consumer.Unapproved")
            .LogWarning("Unapproved synthetic-secret {Value}", "private-value");
        CompleteSampleOperation(provider);
        tracerProvider.ForceFlush();
        Assert.Contains(existing.Records, record => record.Category == "Consumer.Unapproved");
        Assert.Contains(existing.Records, record => record.Category == OnuObservabilityDefaults.SafeLogCategory);
        Assert.DoesNotContain(exported, record => record.CategoryName == "Consumer.Unapproved");
        var safe = Assert.Single(exported, record =>
            record.CategoryName == OnuObservabilityDefaults.SafeLogCategory);
        Assert.Null(safe.FormattedMessage);
        Assert.Null(safe.Exception);
        Assert.DoesNotContain("synthetic-secret", safe.Body ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("private-value", safe.Body ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(Assert.Single(activities).TraceId, safe.TraceId);
    }

    private static void CompleteSampleOperation(IServiceProvider provider)
    {
        var descriptor = provider.GetRequiredService<TelemetrySchemaSnapshot>()
            .Operations.Single(operation => operation.Name == "sample.operation");
        using var scope = provider.GetRequiredService<IOperationFactory>().Start(descriptor);
        scope.Complete(OperationResult.Success);
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _records = new();

        public IReadOnlyCollection<LogEntry> Records => _records.ToArray();

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, _records);

        public void Dispose()
        {
        }
    }

    private sealed class RecordingLogger(
        string category,
        ConcurrentQueue<LogEntry> records) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            records.Enqueue(new(category, eventId, formatter(state, exception)));
    }

    private sealed record LogEntry(string Category, EventId EventId, string Message);
}
