# Dashboards and provisional SLOs

Start with request/tool/background operation rate, error and cancellation outcomes, p50/p95/p99 duration, in-flight work, queue utilization, dropped/refused/export-failure counts, collector memory, and shutdown-flush failures. Split only by registered bounded dimensions.

Until workload evidence is collected, use provisional objectives rather than promises: no protocol corruption or application failure caused by telemetry; zero sensitive canary occurrences; zero sustained queue saturation; exporter recovery without restart; and package shutdown within its configured budget. Application teams own latency/error SLO thresholds because workload and dependency behavior are not package properties.
