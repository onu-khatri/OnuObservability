using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace OnuObservability.Mcp;

internal sealed class McpFilterRuntime
{
    private readonly TaskCompletionSource<Filters> _ready = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public void Initialize(
        McpRequestTelemetryFilter request,
        McpToolTelemetryFilter tool)
    {
        if (!_ready.TrySetResult(new(request, tool)))
        {
            throw new InvalidOperationException("MCP telemetry filters are already initialized.");
        }
    }

    public async Task InvokeRequestAsync(
        McpMessageHandler next,
        MessageContext context,
        CancellationToken cancellationToken)
    {
        var filters = await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        await filters.Request.InvokeAsync(next, context, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<CallToolResult> InvokeToolAsync(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken)
    {
        var filters = await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await filters.Tool.InvokeAsync(next, context, cancellationToken).ConfigureAwait(false);
    }

    private sealed record Filters(
        McpRequestTelemetryFilter Request,
        McpToolTelemetryFilter Tool);
}
