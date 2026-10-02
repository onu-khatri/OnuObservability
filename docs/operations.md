# Collector deployment and runbooks

Use the assets and commands in [the collector README](../observability/README.md). Defaults bind OTLP, health, and self-metrics to loopback, cap collector memory, put the memory limiter first, sanitize attributes/resources, batch with finite sizes, and use bounded non-blocking queues/retries.

For exporter or backend outage, confirm application work continues, then inspect `onu.telemetry.dropped`, Collector refused/enqueue/send-failed metrics, queue size/capacity, and bounded stderr diagnostics. Restore the backend before retry windows expire. Do not increase queues or retry duration without a memory/latency review.

For shutdown, allow the configured package flush budget. A non-cooperative exporter is abandoned after that budget so host exit can proceed. For cardinality growth, identify the unregistered caller-derived input and restore descriptor/fallback use; do not raise limits as the first response.

For privacy incidents, stop capture/export if safe, preserve only approved incident evidence, rotate exposed credentials, review application capture points and collector rules, and follow the deployment's retention and deletion procedure.
