# Performance budgets

The initial operation-boundary budgets are deliberately provisional and are release gates, not service SLOs. Run the benchmark in Release mode on an otherwise idle machine:

```powershell
dotnet run --project benchmarks/OnuObservability.Benchmarks -c Release -- --output artifacts/benchmarks/latest.json
```

Each profile warms up 2,000 operations and samples 25,000 operations. The JSON report records p50, p95, p99, throughput, process CPU time, and allocation per operation. A run fails when either p95 or allocation exceeds its profile budget.

| Profile | p95 ceiling | Allocation ceiling |
|---|---:|---:|
| Disabled | 50 microseconds | 2,048 bytes/operation |
| Local signals, no exporter | 100 microseconds | 2,048 bytes/operation |
| Bounded batched OTLP | 150 microseconds | 4,096 bytes/operation |

Compare changes on the same runner class and retain the machine-readable report. These thresholds must be reviewed with representative service workloads before stable `1.0.0` promotion.
