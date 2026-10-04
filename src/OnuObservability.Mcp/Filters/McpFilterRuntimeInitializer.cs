using Microsoft.Extensions.Hosting;

namespace OnuObservability.Mcp.Filters;

internal sealed class McpFilterRuntimeInitializer(
    McpFilterRuntime runtime,
    McpRequestTelemetryFilter request,
    McpToolTelemetryFilter tool) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        runtime.Initialize(request, tool);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
