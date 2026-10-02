# Adapter guides

## ASP.NET Core

Reference `OnuObservability.AspNetCore`, call `AddOnuObservability(...).AddOnuAspNetCore()`, then `UseOnuObservability()`. Endpoint route templates are discovered automatically, sorted deterministically, capped at 1024, and overflow maps to `other`. Configure exact exclusions for health or metrics endpoints when those requests should produce no adapter telemetry.

## Background services

Reference `OnuObservability.Hosting`. Register a stable descriptor during startup and run a whole pass or batch through `IBackgroundOperationRunner`. Provide only aggregate counts. Host stop, caller cancellation, deadline, validation, dependency failure, and internal failure use typed inputs and remain distinct. Do not create one operation per record.

## MCP servers

Reference `OnuObservability.Mcp` and call `WithOnuObservability` on the configured MCP builder. Tool identity comes from the same SDK descriptors used by `tools/list`. Use `IMcpTelemetryOutcomeContext` or a custom `IMcpTelemetryOutcomeClassifier` for bounded domain classification. Stdio-safe mode disables only the console logger provider, keeps MCP stdout protocol-only, and sends approved JSON diagnostics to stderr.

See the executable projects under `../samples` for minimal compositions.
