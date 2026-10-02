# Signal catalog

## Core and Hosting owner

Activity source and meter: `OnuObservability`. Registered application operation names become internal span names only after exact startup registration.

| Signal | Type/unit | Bounded dimensions |
|---|---|---|
| `onu.operation.count` | counter, `{operation}` | `operation`, `outcome` |
| `onu.operation.duration` | histogram, `s` | `operation`, `outcome` |
| `onu.telemetry.dropped` | counter, `{item}` | finite signal and reason |
| `onu.operation.completed` / event 1000 | log | registered operation/outcome, duration |
| `onu.operation.failed` / event 1001 | log | registered operation/outcome/error |
| `onu.log.suppression_summary` / event 1002 | log | registered event, bounded count |

Resources contain `service.name`, `service.namespace`, optional `service.version`, `deployment.environment.name`, runtime identity, `telemetry.schema.version`, and `telemetry.policy.version`. Traces/logs also contain `service.instance.id`; metrics deliberately omit it.

Background fields owned by Hosting are `work.processed`, `work.succeeded`, `work.failed`, `work.deferred`, `work.retried`, and `work.remaining`, each clamped to 0–1,000,000,000.

## ASP.NET Core owner

Meter: `OnuObservability.AspNetCore`. Metrics are `onu.http.server.request.count` (`{request}`) and `onu.http.server.request.duration` (`s`). Dimensions are bounded HTTP method, registered route template or `other`, status class, and outcome. Existing server activities are enriched; raw paths, route values, queries, headers, bodies, addresses, and exception content are removed or excluded.

## MCP owner

Activity source/meter remain `AgentSession.MCP` for compatibility. The authoritative metric, activity, histogram, method, protocol, and event manifest is `McpTelemetrySchema`; its automated baseline is in `../tests` and the parent change evidence. Tool names come only from the SDK registry or `unknown`. Event IDs are 1001 completed, 1002 failed, 1003 suppression, 1090 dropped, and 1101 destructive attempt.

Applications own domain dependency signals. The MCP adapter catalog retains compatibility descriptors, but does not implement AgentMemory-specific Ollama, Qdrant, filesystem, or maintenance operations.
