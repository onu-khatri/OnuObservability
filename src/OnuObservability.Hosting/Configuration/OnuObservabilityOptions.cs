namespace OnuObservability.Hosting.Configuration;

public sealed class OnuObservabilityOptions
{
    public const string SectionName = "OnuObservability";

    public bool Enabled { get; set; } = true;
    public bool ExporterEnabled { get; set; }
    public bool ExportTraces { get; set; } = true;
    public bool ExportMetrics { get; set; } = true;
    public bool ExportLogs { get; set; }
    public bool LocalJsonLoggingEnabled { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string ServiceNamespace { get; set; } = "onu";
    public string? ServiceVersion { get; set; }
    public string DeploymentEnvironment { get; set; } = "development";
    public string TelemetrySchemaVersion { get; set; } = "1.0";
    public string PolicyVersion { get; set; } = "otel-stable-1";
    public string ExporterProtocol { get; set; } = "grpc";
    public string? Endpoint { get; set; }
    public string[] InsecureSidecarHosts { get; set; } = [];
    public double TraceSamplingRatio { get; set; } = 1;
    public int ExportTimeoutMilliseconds { get; set; } = 10_000;
    public int BatchDelayMilliseconds { get; set; } = 5_000;
    public int QueueCapacity { get; set; } = 2_048;
    public int MaximumExportBatchSize { get; set; } = 512;
    public int ShutdownFlushTimeoutMilliseconds { get; set; } = 5_000;
    public int MaximumLogBodyLength { get; set; } = 1_024;
    public int MaximumAttributeLength { get; set; } = 256;
    public int LogRateLimitWindowSeconds { get; set; } = 60;
    public int LogRateLimitBurst { get; set; } = 20;
    public int MaximumLogRateLimitKeys { get; set; } = 256;
}

public enum OtlpTransportProtocol
{
    Grpc,
    HttpProtobuf,
}

public sealed record EffectiveObservabilitySettings(
    bool Enabled,
    bool ExporterEnabled,
    bool ExportTraces,
    bool ExportMetrics,
    bool ExportLogs,
    bool LocalJsonLoggingEnabled,
    string ServiceName,
    string ServiceNamespace,
    string? ServiceVersion,
    string DeploymentEnvironment,
    string TelemetrySchemaVersion,
    string PolicyVersion,
    OtlpTransportProtocol ExporterProtocol,
    Uri? Endpoint,
    IReadOnlySet<string> InsecureSidecarHosts,
    double TraceSamplingRatio,
    TimeSpan ExportTimeout,
    TimeSpan BatchDelay,
    int QueueCapacity,
    int MaximumExportBatchSize,
    TimeSpan ShutdownFlushTimeout,
    int MaximumLogBodyLength,
    int MaximumAttributeLength,
    TimeSpan LogRateLimitWindow,
    int LogRateLimitBurst,
    int MaximumLogRateLimitKeys);
