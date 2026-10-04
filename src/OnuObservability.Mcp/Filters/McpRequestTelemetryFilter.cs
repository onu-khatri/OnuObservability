using System.Diagnostics;
using System.Diagnostics.Metrics;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OnuObservability.Mcp.Classification;
using OnuObservability.Mcp.Schema;
using OnuObservability.Mcp.Telemetry;
using OnuObservability.Model;

namespace OnuObservability.Mcp.Filters;

public sealed class McpRequestTelemetryFilter(
    McpTelemetryInstruments telemetry,
    IMcpTelemetryOutcomeClassifier classifier,
    TimeProvider timeProvider)
{
    public Task InvokeAsync(
        McpMessageHandler next,
        MessageContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(context);
        if (context.JsonRpcMessage is not JsonRpcRequest request)
        {
            return next(context, cancellationToken);
        }

        var protocolVersion = context.JsonRpcMessage.Context?.ProtocolVersion ??
                              context.Server.NegotiatedProtocolVersion;
        return InvokeCoreAsync(
            request.Method,
            protocolVersion,
            token => next(context, token),
            cancellationToken);
    }

    internal async Task InvokeCoreAsync(
        string? methodValue,
        string? protocolVersionValue,
        Func<CancellationToken, Task> next,
        CancellationToken cancellationToken)
    {
        var method = McpTelemetrySchema.NormalizeMethod(methodValue);
        var protocol = McpTelemetrySchema.NormalizeProtocolVersion(protocolVersionValue);
        var started = timeProvider.GetTimestamp();
        var classification = McpTelemetryOutcomeClassifier.Success;
        var previousActivity = Activity.Current;
        Activity.Current = null;
        using var activity = McpTelemetrySources.Activities.StartActivity(
            "mcp request",
            ActivityKind.Server);
        activity?.SetTag("mcp.method.name", method);
        activity?.SetTag("mcp.protocol.version", protocol);
        activity?.SetTag("mcp.transport", McpTelemetrySchema.StdioTransport);
        telemetry.ServerInflight.Add(
            1,
            new KeyValuePair<string, object?>("mcp.method.name", method));
        try
        {
            await next(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            classification = McpTelemetryValues.Normalize(
                classifier.Classify(exception, cancellationToken));
            RecordSpecializedFailure(method, classification);
            throw;
        }
        finally
        {
            try
            {
                var tags = new TagList
                {
                    { "mcp.method.name", method },
                    { "mcp.status", classification.Outcome.Value },
                    { "mcp.protocol.version", protocol },
                    { "mcp.transport", McpTelemetrySchema.StdioTransport },
                };
                activity?.SetTag("mcp.status", classification.Outcome.Value);
                if (classification.Outcome != CommonOutcomes.Success)
                {
                    activity?.SetTag("error.type", McpTelemetryValues.NormalizeError(classification.Error?.Value));
                    activity?.SetStatus(ActivityStatusCode.Error);
                }

                telemetry.ServerRequests.Add(1, tags);
                telemetry.ServerRequestDuration.Record(
                    timeProvider.GetElapsedTime(started).TotalSeconds,
                    new("mcp.method.name", method),
                    new("mcp.status", classification.Outcome.Value));
                telemetry.ServerInflight.Add(
                    -1,
                    new KeyValuePair<string, object?>("mcp.method.name", method));
            }
            finally
            {
                Activity.Current = previousActivity;
            }
        }
    }

    private void RecordSpecializedFailure(
        string method,
        McpTelemetryClassification classification)
    {
        if (classification.Outcome == McpTelemetrySchema.ProtocolErrorOutcome)
        {
            telemetry.ProtocolErrors.Add(1,
                new("mcp.method.name", method),
                new("reason", McpTelemetryValues.NormalizeError(classification.Error?.Value)));
        }
        else if (classification.Outcome == CommonOutcomes.DeadlineExceeded)
        {
            telemetry.ServerTimeouts.Add(1,
                new("mcp.method.name", method),
                new("mcp.tool.name", McpTelemetrySchema.Unknown));
        }
        else if (classification.Outcome == McpTelemetrySchema.ClientCancelledOutcome)
        {
            telemetry.ServerCancelled.Add(
                1,
                new KeyValuePair<string, object?>("mcp.method.name", method));
        }
    }
}
