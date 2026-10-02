# Collector assets

These profiles run a pinned OpenTelemetry Collector Contrib image with bounded memory, batches, queues, retry windows, and defense-in-depth deletion of sensitive attributes.

- `collector.local.yaml` writes to container tmpfs and is disposable.
- `collector.production.yaml` exports to an HTTPS backend configured through environment variables.
- `collector.persistent.yaml` enables a bounded file-backed queue.
- `collector.failure-test.yaml` uses deliberately small failure bounds for resilience tests.
- `collector.test-capture.yaml` is a test-only overlay for an explicitly approved temporary capture directory.

Start, inspect, and stop local mode from the repository root:

```powershell
./scripts/Start-OnuObservability.ps1 -Mode Local
./scripts/Check-OnuObservability.ps1
./scripts/Stop-OnuObservability.ps1 -Mode Local
```

Production and persistent modes require `OTEL_BACKEND_ENDPOINT` with HTTPS and `OTEL_BACKEND_AUTHORIZATION` supplied by the process environment or a secret manager. Persistent mode can retain operationally sensitive telemetry after containers stop. Approve storage location, access, encryption, retention, incident response, and secure deletion before using it; the stop script deliberately retains the volume.

All published host ports bind to `127.0.0.1`. Exposing OTLP, health, or collector self-metrics beyond loopback is a deployment decision outside these reusable defaults.

After packing and validating local packages, run the exact-package Web capture test with:

```powershell
./eng/Test-WebLiveCollector.ps1
```

The harness allocates loopback ports, uses a unique non-persistent capture directory under `artifacts/live-collector`, verifies W3C propagation and bounded Web signals, checks that its route-value canary is absent, and always removes its temporary Compose project.
