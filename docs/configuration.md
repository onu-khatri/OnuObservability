# Configuration and public API

Call `services.AddOnuObservability(configuration)` once, add the required adapter, then build the host. Configuration uses the `OnuObservability` section. Remote export is disabled by default. Invalid enabled-export settings fail startup without reproducing the supplied endpoint or credentials.

## Hosting options

| Option | Default | Allowed / purpose |
|---|---:|---|
| `Enabled` | `true` | Enables the package pipeline. |
| `ExporterEnabled` | `false` | Enables remote OTLP only when an endpoint is valid. |
| `ExportTraces` | `true` | Enables trace export. |
| `ExportMetrics` | `true` | Enables metric export. |
| `ExportLogs` | `false` | Enables approved structured-log export. |
| `LocalJsonLoggingEnabled` | `false` | Writes approved JSON events to stderr. |
| `ServiceName` | required | Safe identifier, at most 128 characters. |
| `ServiceNamespace` | `onu` | Safe identifier, at most 128 characters. |
| `ServiceVersion` | unset | Optional safe identifier. |
| `DeploymentEnvironment` | `development` | Safe environment identifier. |
| `TelemetrySchemaVersion` | `1.0` | Resource schema version. |
| `PolicyVersion` | `otel-stable-1` | Resource privacy/policy version. |
| `ExporterProtocol` | `grpc` | `grpc` or `http/protobuf`. |
| `Endpoint` | unset | Absolute HTTP(S) URI without credentials, query, or fragment. |
| `InsecureSidecarHosts` | empty | Explicit bounded hosts allowed to use clear-text OTLP. |
| `TraceSamplingRatio` | `1` | Finite value from 0 through 1; metrics remain unsampled. |
| `ExportTimeoutMilliseconds` | `10000` | 1–120000. |
| `BatchDelayMilliseconds` | `5000` | 1–60000. |
| `QueueCapacity` | `2048` | 1–65536. |
| `MaximumExportBatchSize` | `512` | 1 through the configured queue capacity. |
| `ShutdownFlushTimeoutMilliseconds` | `5000` | 1–120000 total package budget. |
| `MaximumLogBodyLength` | `1024` | 64–16384. |
| `MaximumAttributeLength` | `256` | 16–4096. |
| `LogRateLimitWindowSeconds` | `60` | 1–3600. |
| `LogRateLimitBurst` | `20` | 1–10000. |
| `MaximumLogRateLimitKeys` | `256` | 1–10000. |

Supported `OTEL_*` values override the package section: `OTEL_SDK_DISABLED`, `OTEL_SERVICE_NAME`, `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_PROTOCOL`, `OTEL_TRACES_SAMPLER_ARG`, `OTEL_TRACES_EXPORTER`, `OTEL_METRICS_EXPORTER`, `OTEL_LOGS_EXPORTER`, and the `service.namespace` / deployment-environment entries in `OTEL_RESOURCE_ATTRIBUTES`.

## ASP.NET Core options

Under `OnuObservability:AspNetCore`, `InstrumentRequests` defaults to `true`, `MaximumRouteTemplates` defaults to and cannot exceed 1024, and `ExcludedRouteTemplates` contains at most 1024 exact templates of at most 256 characters. Routes are discovered automatically.

## MCP options

Under `OnuObservability:Mcp`, `StdioSafeLogging` defaults to `true`; `MaximumInputItems` and `MaximumOutputItems` default to 256 and allow 1–10000; `LogRateLimitBurst` defaults to 20; `LogRateLimitWindowSeconds` defaults to 60; and `MaximumLogRateLimitKeys` defaults to 512. MCP count values are clamped and payloads are never inspected for telemetry.

## Registration API

- Hosting: `AddOnuObservability`, then register application `AddOperation`, `AddEvent`, `AddOutcome`, `AddError`, `AddReason`, or `AddSafeLogPolicy` entries before the service provider is built. Outbound HTTP tracing is disabled unless the consumer calls `AddOutboundHttpClientInstrumentation` with a reviewed finite request predicate; the package removes raw URL, path, query, host, port, and authorization tags before export.
- Web: `AddOnuAspNetCore`, then `UseOnuObservability` after routing information is available.
- MCP: call `WithOnuObservability` on the same `IMcpServerBuilder` that owns the tools.
- Worker: inject `IBackgroundOperationRunner` and use one registered descriptor per coarse pass, batch, or dependency boundary.
