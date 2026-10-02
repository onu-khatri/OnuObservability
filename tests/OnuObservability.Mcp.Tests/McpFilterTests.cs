using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OnuObservability.Model;

namespace OnuObservability.Mcp.Tests;

public sealed class McpFilterTests
{
    [Fact]
    public async Task ClassifiedExceptionBecomesTypedToolErrorResult()
    {
        var tool = Tool("sample_tool");
        var registry = new McpRegisteredToolRegistry([tool]);
        using var instruments = new McpTelemetryInstruments();
        var context = new McpTelemetryOutcomeContext();
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var filter = new McpToolTelemetryFilter(
            loggerFactory,
            instruments,
            registry,
            context,
            new McpTelemetryOutcomeClassifier(),
            new McpObservabilityOptions(),
            TimeProvider.System);
        var classification = new McpTelemetryClassification(
            CommonOutcomes.ValidationError,
            new ErrorCode("invalid_request"),
            McpValidationReason.DomainRule);

        var result = await filter.InvokeCoreAsync(
            "sample_tool",
            inputItems: 1,
            _ => throw McpClassifiedException.Create(
                "invalid_request: Safe client message.",
                classification,
                context),
            default);

        Assert.True(result.IsError);
        var content = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal("invalid_request: Safe client message.", content.Text);
    }

    [Theory]
    [InlineData("2025-11-25", "2025-11-25")]
    [InlineData("2026-07-28", "2026-07-28")]
    [InlineData(null, "unknown")]
    [InlineData("malformed-private-value", "other")]
    public async Task RequestAndToolFiltersPreserveHierarchyAndBoundProtocol(
        string? inputProtocol,
        string expectedProtocol)
    {
        var tool = Tool("sample_tool");
        var registry = new McpRegisteredToolRegistry([tool]);
        using var instruments = new McpTelemetryInstruments();
        var outcomeContext = new McpTelemetryOutcomeContext();
        var classifier = new McpTelemetryOutcomeClassifier();
        var logs = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));
        var toolFilter = new McpToolTelemetryFilter(
            loggerFactory,
            instruments,
            registry,
            outcomeContext,
            classifier,
            new McpObservabilityOptions(),
            TimeProvider.System);
        var requestFilter = new McpRequestTelemetryFilter(
            instruments,
            classifier,
            TimeProvider.System);
        using var activities = new ActivityCapture();
        using var metrics = new MetricCapture();

        await requestFilter.InvokeCoreAsync(
            "tools/call",
            inputProtocol,
            async token =>
            {
                await toolFilter.InvokeCoreAsync(
                    "sample_tool",
                    inputItems: 2,
                    _ => ValueTask.FromResult(new CallToolResult { Content = [] }),
                    token);
            },
            default);

        var request = Assert.Single(activities.Stopped, value => value.DisplayName == "mcp request");
        var toolActivity = Assert.Single(
            activities.Stopped,
            value => value.DisplayName == "mcp tool sample_tool");
        Assert.Equal(request.SpanId, toolActivity.ParentSpanId);
        Assert.Equal(expectedProtocol, request.GetTagItem("mcp.protocol.version"));
        var inflight = metrics.LongValues("mcp.server.inflight").ToArray();
        Assert.Contains(1, inflight);
        Assert.Contains(-1, inflight);
        Assert.Equal(0, inflight.Sum());
        Assert.Single(logs.Entries, entry => entry.EventId.Id == 1001);
    }

    [Fact]
    public async Task TypedDomainClassificationSurvivesErrorResultConversion()
    {
        const string renderedError = "private-rendered-client-error";
        var registry = new McpRegisteredToolRegistry([Tool("sample_tool")]);
        using var instruments = new McpTelemetryInstruments();
        var context = new McpTelemetryOutcomeContext();
        var classification = new McpTelemetryClassification(
            CommonOutcomes.AuthorizationDenied,
            new ErrorCode("policy_denied"),
            DenialReason: McpDenialReason.PolicyDenied);
        var logs = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));
        var filter = new McpToolTelemetryFilter(
            loggerFactory,
            instruments,
            registry,
            context,
            new McpTelemetryOutcomeClassifier(),
            new McpObservabilityOptions(),
            TimeProvider.System);
        using var activities = new ActivityCapture();

        var result = await filter.InvokeCoreAsync(
            "sample_tool",
            inputItems: 1,
            _ =>
            {
                context.Record(classification);
                return ValueTask.FromResult(new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock { Text = renderedError }],
                });
            },
            default);

        Assert.True(result.IsError);
        var activity = Assert.Single(activities.Stopped);
        Assert.Equal("authorization_denied", activity.GetTagItem("mcp.status"));
        Assert.Equal("policy_denied", activity.GetTagItem("error.type"));
        Assert.DoesNotContain(
            renderedError,
            string.Join('|', activity.TagObjects.Select(tag => tag.Value)),
            StringComparison.Ordinal);
        Assert.Single(logs.Entries, entry => entry.EventId.Id == 1002);
        Assert.DoesNotContain(
            renderedError,
            string.Join('|', logs.Entries.Select(entry => entry.Message)),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task UniqueCallerOutcomesCollapseBeforeMetricsActivitiesAndLogs()
    {
        var registry = new McpRegisteredToolRegistry([Tool("sample_tool")]);
        using var instruments = new McpTelemetryInstruments();
        var context = new McpTelemetryOutcomeContext();
        var logs = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));
        var filter = new McpToolTelemetryFilter(
            loggerFactory,
            instruments,
            registry,
            context,
            new McpTelemetryOutcomeClassifier(),
            new McpObservabilityOptions { LogRateLimitBurst = 10_000 },
            TimeProvider.System);
        using var activities = new ActivityCapture();
        using var metrics = new MetricCapture();

        for (var index = 0; index < 2_000; index++)
        {
            await filter.InvokeCoreAsync(
                "caller-tool-" + index,
                inputItems: index,
                _ =>
                {
                    context.Record(new(
                        new Outcome("caller_outcome_" + index),
                        new ErrorCode("caller_error_" + index)));
                    return ValueTask.FromResult(new CallToolResult
                    {
                        IsError = true,
                        Content = [],
                    });
                },
                default);
        }

        Assert.All(activities.Stopped, activity =>
        {
            Assert.Equal("mcp tool unknown", activity.DisplayName);
            Assert.Equal("unknown", activity.GetTagItem("mcp.tool.name"));
            Assert.Equal("other", activity.GetTagItem("mcp.status"));
            Assert.Equal("other", activity.GetTagItem("error.type"));
        });
        Assert.All(metrics.TagValues("mcp.tool.invocations", "mcp.tool.name"),
            value => Assert.Equal("unknown", value));
        Assert.All(metrics.TagValues("mcp.tool.invocations", "mcp.status"),
            value => Assert.Equal("other", value));
        Assert.DoesNotContain("caller_outcome_", string.Join('|', logs.Entries.Select(entry => entry.Message)));
        Assert.DoesNotContain("caller_error_", string.Join('|', logs.Entries.Select(entry => entry.Message)));
    }

    [Fact]
    public async Task RateLimiterCapacityIsBoundedUnderConcurrentUniqueInputs()
    {
        var tools = Enumerable.Range(0, 100).Select(index => Tool($"tool_{index}")).ToArray();
        var limiter = new McpLogRateLimiter(
            new McpRegisteredToolRegistry(tools),
            new McpObservabilityOptions
            {
                LogRateLimitBurst = 1,
                MaximumLogRateLimitKeys = 8,
            },
            TimeProvider.System);

        await Task.WhenAll(Enumerable.Range(0, 2_000).Select(index => Task.Run(() =>
            limiter.Acquire(
                "caller_event_" + index,
                "tool_" + (index % tools.Length),
                new Outcome("caller_outcome_" + index)))));

        Assert.InRange(limiter.KeyCount, 1, 8);
    }

    private static McpServerTool Tool(string name) => McpServerTool.Create(
        (Func<string>)(() => "ok"),
        new McpServerToolCreateOptions { Name = name });

    private sealed class ActivityCapture : IDisposable
    {
        private readonly ActivityListener _listener;

        public ActivityCapture()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == McpTelemetrySchema.ActivitySourceName,
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = Stopped.Add,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public ConcurrentBag<Activity> Stopped { get; } = [];

        public void Dispose() => _listener.Dispose();
    }

    private sealed class MetricCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentBag<MetricMeasurement> _longValues = [];

        public MetricCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == McpTelemetrySchema.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
                _longValues.Add(new(instrument.Name, value, tags.ToArray())));
            _listener.Start();
        }

        public IEnumerable<long> LongValues(string name) =>
            _longValues.Where(value => value.Name == name).Select(value => value.Value);

        public IEnumerable<string?> TagValues(string instrumentName, string tagName) =>
            _longValues
                .Where(value => value.Name == instrumentName)
                .SelectMany(value => value.Tags)
                .Where(tag => tag.Key == tagName)
                .Select(tag => tag.Value?.ToString());

        public void Dispose() => _listener.Dispose();

        private sealed record MetricMeasurement(
            string Name,
            long Value,
            KeyValuePair<string, object?>[] Tags);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<LogEntry> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Logger(Entries);

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentBag<LogEntry> entries) : ILogger
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
                entries.Add(new(eventId, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(EventId EventId, string Message);
}
