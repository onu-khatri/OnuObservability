# Ownership

Package responsibilities are owned by boundary:

- Core policy: descriptors, outcomes, bounded values, schema registry, and SDK-independent operation contracts.
- Hosting infrastructure: configuration, resources, OpenTelemetry providers/exporters, health, and shutdown.
- ASP.NET Core adapter: HTTP request enrichment and bounded request policy.
- MCP adapter: request/tool filters, registered-tool bridge, typed outcome context, and stdio-safe diagnostics.
- Consumer applications: domain operations, domain error mappings, service-specific dashboards, and deployment policy.

The initial package owner and maintainer identity is `onu-khatri`. Ownership changes require an explicit repository decision and a corresponding package-metadata update.
