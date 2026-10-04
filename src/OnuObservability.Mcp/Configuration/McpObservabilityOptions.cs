namespace OnuObservability.Mcp.Configuration;

public sealed class McpObservabilityOptions
{
    public bool StdioSafeLogging { get; set; } = true;

    public int MaximumInputItems { get; set; } = 256;

    public int MaximumOutputItems { get; set; } = 256;

    public int LogRateLimitBurst { get; set; } = 20;

    public int LogRateLimitWindowSeconds { get; set; } = 60;

    public int MaximumLogRateLimitKeys { get; set; } = 512;
}
