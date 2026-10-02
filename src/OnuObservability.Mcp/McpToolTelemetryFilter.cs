using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OnuObservability.Model;

namespace OnuObservability.Mcp;

public sealed class McpToolTelemetryFilter
{
    private readonly IMcpTelemetryOutcomeClassifier _classifier;
    private readonly ILogger _logger;
    private readonly McpLogRateLimiter _logRateLimiter;
    private readonly int _maximumInputItems;
    private readonly int _maximumOutputItems;
    private readonly IMcpTelemetryOutcomeContext _outcomeContext;
    private readonly McpTelemetryInstruments _telemetry;
    private readonly TimeProvider _timeProvider;
    private readonly McpRegisteredToolRegistry _tools;

    public McpToolTelemetryFilter(
        ILoggerFactory loggerFactory,
        McpTelemetryInstruments telemetry,
        McpRegisteredToolRegistry tools,
        IMcpTelemetryOutcomeContext outcomeContext,
        IMcpTelemetryOutcomeClassifier classifier,
        McpObservabilityOptions options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _outcomeContext = outcomeContext ?? throw new ArgumentNullException(nameof(outcomeContext));
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        ArgumentNullException.ThrowIfNull(options);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        Validate(options);
        _maximumInputItems = options.MaximumInputItems;
        _maximumOutputItems = options.MaximumOutputItems;
        _logger = loggerFactory.CreateLogger(McpTelemetrySchema.SafeLogCategory);
        _logRateLimiter = new(tools, options, timeProvider);
    }

    public ValueTask<CallToolResult> InvokeAsync(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(context);
        var tool = _tools.Resolve(context.MatchedPrimitive);
        var inputItems = Math.Min(context.Params?.Arguments?.Count ?? 0, _maximumInputItems);
        return InvokeCoreAsync(
            tool,
            inputItems,
            token => next(context, token),
            cancellationToken,
            _tools.IsDestructive(context.MatchedPrimitive));
    }

    internal async ValueTask<CallToolResult> InvokeCoreAsync(
        string toolName,
        int inputItems,
        Func<CancellationToken, ValueTask<CallToolResult>> next,
        CancellationToken cancellationToken,
        bool isDestructive = false)
    {
        toolName = _tools.Names.Contains(toolName, StringComparer.Ordinal)
            ? toolName
            : McpTelemetrySchema.Unknown;
        inputItems = Math.Clamp(inputItems, 0, _maximumInputItems);
        var outputItems = 0;
        var started = _timeProvider.GetTimestamp();
        var classification = McpTelemetryOutcomeClassifier.Success;
        using var outcomeScope = _outcomeContext.BeginScope();
        using var activity = McpTelemetrySources.Activities.StartActivity(
            "mcp tool " + toolName,
            ActivityKind.Internal);
        activity?.SetTag("mcp.tool.name", toolName);
        activity?.SetTag("mcp.transport", McpTelemetrySchema.StdioTransport);

        try
        {
            var result = await next(cancellationToken).ConfigureAwait(false);
            outputItems = Math.Min(result.Content?.Count ?? 0, _maximumOutputItems);
            if (result.IsError is true)
            {
                classification = McpTelemetryValues.Normalize(outcomeScope.Classification ??
                    new McpTelemetryClassification(
                        McpTelemetrySchema.ToolErrorOutcome,
                        new("mcp_error")));
            }

            return result;
        }
        catch (McpClassifiedException exception)
        {
            classification = McpTelemetryValues.Normalize(exception.Classification);
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = exception.Message }],
            };
        }
        catch (Exception exception)
        {
            classification = McpTelemetryValues.Normalize(
                _classifier.Classify(exception, cancellationToken));
            throw;
        }
        finally
        {
            try
            {
                Record(
                    activity,
                    toolName,
                    classification,
                    _timeProvider.GetElapsedTime(started).TotalSeconds,
                    inputItems,
                    outputItems,
                    isDestructive);
            }
            catch (Exception)
            {
                // Telemetry is intentionally fail-open and must never replace the MCP result.
            }
        }
    }

    private void Record(
        Activity? activity,
        string toolName,
        McpTelemetryClassification classification,
        double durationSeconds,
        int inputItems,
        int outputItems,
        bool isDestructive)
    {
        var tags = new TagList
        {
            { "mcp.tool.name", toolName },
            { "mcp.status", classification.Outcome.Value },
        };
        activity?.SetTag("mcp.status", classification.Outcome.Value);
        if (classification.Outcome != CommonOutcomes.Success)
        {
            activity?.SetTag("error.type", McpTelemetryValues.NormalizeError(classification.Error?.Value));
            activity?.SetStatus(ActivityStatusCode.Error);
        }

        _telemetry.ToolInvocations.Add(1, tags);
        _telemetry.ToolDuration.Record(durationSeconds, tags);
        _telemetry.ToolInputItems.Record(
            inputItems,
            new KeyValuePair<string, object?>("mcp.tool.name", toolName));
        _telemetry.ToolOutputItems.Record(
            outputItems,
            new KeyValuePair<string, object?>("mcp.tool.name", toolName));
        RecordSpecialized(classification, toolName);

        if (isDestructive)
        {
            McpSafeLogEvents.DestructiveToolAttempt(
                _logger,
                toolName,
                classification.Outcome.Value);
        }

        var eventName = classification.Outcome == CommonOutcomes.Success
            ? "mcp.tool.completed"
            : "mcp.tool.failed";
        var decision = _logRateLimiter.Acquire(eventName, toolName, classification.Outcome);
        if (decision.Suppressed > 0)
        {
            McpSafeLogEvents.LogsSuppressed(
                _logger,
                eventName,
                toolName,
                classification.Outcome.Value,
                decision.Suppressed);
        }

        if (!decision.Allowed)
        {
            return;
        }

        if (classification.Outcome == CommonOutcomes.Success)
        {
            McpSafeLogEvents.ToolCompleted(
                _logger,
                toolName,
                classification.Outcome.Value,
                durationSeconds,
                inputItems,
                outputItems);
        }
        else
        {
            McpSafeLogEvents.ToolFailed(
                _logger,
                toolName,
                classification.Outcome.Value,
                McpTelemetryValues.NormalizeError(classification.Error?.Value),
                durationSeconds,
                inputItems,
                outputItems);
        }
    }

    private void RecordSpecialized(
        McpTelemetryClassification classification,
        string toolName)
    {
        if (classification.ValidationReason is { } validation)
        {
            _telemetry.ValidationFailures.Add(1,
                new("mcp.tool.name", toolName),
                new("reason", McpTelemetryValues.ValidationReason(validation)));
        }

        if (classification.DenialReason is { } denial)
        {
            _telemetry.AuthorizationDenials.Add(1,
                new("mcp.tool.name", toolName),
                new("reason", McpTelemetryValues.DenialReason(denial)));
        }

        if (classification.Outcome == CommonOutcomes.DeadlineExceeded)
        {
            _telemetry.ServerTimeouts.Add(1,
                new("mcp.method.name", "tools/call"),
                new("mcp.tool.name", toolName));
        }
        else if (classification.Outcome == McpTelemetrySchema.ClientCancelledOutcome)
        {
            _telemetry.ServerCancelled.Add(
                1,
                new KeyValuePair<string, object?>("mcp.method.name", "tools/call"));
        }
    }

    private static void Validate(McpObservabilityOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaximumInputItems, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaximumInputItems, 10_000);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaximumOutputItems, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaximumOutputItems, 10_000);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.LogRateLimitBurst, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.LogRateLimitWindowSeconds, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaximumLogRateLimitKeys, 1);
    }
}
