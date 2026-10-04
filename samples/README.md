# Samples

Three self-contained, runnable projects demonstrate the full `OnuObservability` API
surface for each supported host type. Each sample uses the source packages via project
references (set `UseLocalPackages=true` to consume the published NuGet packages instead).

| Sample | Host type | Adapter | What it demonstrates |
|---|---|---|---|
| [`OnuObservability.Sample.Web`](OnuObservability.Sample.Web) | ASP.NET Core (minimal API) | `OnuObservability.AspNetCore` | Request enrichment, bounded route policy, HTTP outcomes, request-scoped domain operations, outbound HTTP instrumentation |
| [`OnuObservability.Sample.Worker`](OnuObservability.Sample.Worker) | Generic Host (`BackgroundService`) | `OnuObservability.Hosting` | Background operations, dependency boundaries, typed cancellation/outcomes, custom schema, safe-log policy |
| [`OnuObservability.Sample.Mcp`](OnuObservability.Sample.Mcp) | MCP stdio server | `OnuObservability.Mcp` | Tool/request filters, destructive tools, validation/denial classification, custom classifier, dependency recording, stdio-safe logging |

## Scenario coverage

### OnuObservability.Sample.Web

| Scenario | Where |
|---|---|
| Request enrichment (method, route, status class, outcome) | `OnuAspNetCoreMiddleware` (adapter) |
| Route template discovery, deterministic ordering, 1024 cap | `AspNetCoreRouteRegistry` (adapter) |
| Exact route exclusion (health endpoints emit no telemetry) | `appsettings.json` → `ExcludedRouteTemplates: ["/health", "/ready"]` |
| Success / validation / not-found / server-error outcomes | `/`, `/validation`, `/products/{id}`, `/failure` |
| Trace/span correlation via `traceparent` | `/correlation` |
| Request-scoped domain operation with **all** bounded field kinds (finite string, number, count, flag, text) | `OrderTelemetry.cs` + `CheckoutService.cs` via `IOperationFactory` |
| Custom outcomes/errors/reasons registered once at startup | `OrderTelemetry.cs` → `AddOrderTelemetry` |
| Outbound HTTP tracing with a reviewed request predicate + graceful dependency-outage classification | `Program.cs` → `AddOutboundHttpClientInstrumentation`, `/orders/checkout` |

### OnuObservability.Sample.Worker

| Scenario | Where |
|---|---|
| Whole-pass operation with aggregate counts (`processed`, `succeeded`, `failed`, …) | `Worker.cs` + `SampleWorkload` via `IBackgroundOperationRunner.RunAsync` |
| Dependency boundary (`RunDependencyAsync`) | `SampleWorkload.DependencyOutage` |
| Typed outcomes: `success`, `partial`, `dependency_error`, `deadline_exceeded`, `host_stopped` | `SampleWorkload` scenarios |
| Host-stop / deadline / caller-cancellation classification | `BackgroundOperationCancellation` |
| Custom domain outcomes/errors/reasons | `WorkerTelemetry.cs` |
| Custom event manifest (`AddEvent`) | `WorkerTelemetry.cs` |
| Custom safe-log events with an approved-field policy | `WorkerEvents.cs` + `AddSafeLogPolicy` |

### OnuObservability.Sample.Mcp

| Scenario | Where |
|---|---|
| Request/tool telemetry filters (counts, duration, outcome, protocol) | adapter (automatic) |
| Destructive tool (`destructiveHint`) | `SampleTools.Purge` (`Destructive = true`) |
| Validation classification | `SampleTools.Order` → `McpClassifiedException` + `McpValidationReason` |
| Denial classification | `SampleTools.Approve` → `McpClassifiedException` + `McpDenialReason` |
| Custom `IMcpTelemetryOutcomeClassifier` | `SampleOutcomeClassifier.cs` (registered before `WithOnuObservability`) |
| Bounded dependency failure recording | `SampleTools.Search` via `IMcpDependencyFailureRecorder` |
| Stdio-safe logging (JSON diagnostics to stderr, never stdout) | adapter + `StdioSafeLogging` |

## Running

```powershell
# ASP.NET Core (listens on http://localhost:5000)
dotnet run --project samples/OnuObservability.Sample.Web

# Generic Host background worker
dotnet run --project samples/OnuObservability.Sample.Worker

# MCP stdio server (drive it from any MCP client)
dotnet run --project samples/OnuObservability.Sample.Mcp
```

## Remote OTLP export

Remote export is **opt-in** and disabled by default. Each sample ships an
`appsettings.Production.json` that enables it; run with the `Production` environment
to activate it:

```powershell
dotnet run --project samples/OnuObservability.Sample.Web --launch-profile http
# ...with the production config:
$env:ASPNETCORE_ENVIRONMENT = "Production"   # Web
$env:DOTNET_ENVIRONMENT = "Production"       # Worker and MCP
dotnet run --project samples/OnuObservability.Sample.Web
```

The example targets the local collector gated by `observability/collector.local.yaml`
(gRPC `4317`, HTTP `4318`):

```jsonc
{
  "OnuObservability": {
    "ExporterEnabled": true,
    "ExporterProtocol": "grpc",            // or "http/protobuf"
    "Endpoint": "http://localhost:4317",   // 4318 for http/protobuf
    "ExportTraces": true,
    "ExportMetrics": true,
    "ExportLogs": false
  }
}
```

The endpoint must be an absolute HTTP(S) URI without credentials, query, or fragment.
Clear-text (`http://`) export is allowed only to loopback or an explicitly configured
same-host sidecar:

```jsonc
{
  "OnuObservability": {
    "ExporterEnabled": true,
    "ExporterProtocol": "http/protobuf",
    "Endpoint": "http://collector.internal:4318",
    "InsecureSidecarHosts": [ "collector.internal" ]
  }
}
```

The same values can be supplied through standard OpenTelemetry environment variables
instead of (and overriding) the config section, e.g.
`OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_PROTOCOL`, `OTEL_SERVICE_NAME`,
`OTEL_TRACES_SAMPLER_ARG`, and `OTEL_TRACES/METRICS/LOGS_EXPORTER`.

> Privacy note: only operational metadata (finite names/outcomes/errors/reasons,
> bounded counts, durations, status classes, trace/span ids) is ever exported. Raw
> URLs, paths, headers, bodies, addresses, and exception text are removed before export.
