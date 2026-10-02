# Contributing

Changes must preserve the inward dependency graph, metadata-only defaults, bounded cardinality, fail-open application behavior, and package-owned shutdown limits.

1. Add or update tests in the package-owning test project.
2. Update schema and public API baselines for intentional contract changes.
3. Run locked restore, Release build, tests, architecture checks, formatting, and package validation.
4. Record skipped infrastructure checks as unverified.

Do not add caller-derived metric dimensions, raw exception content, service location, mutable global state, cross-adapter references, or OpenTelemetry SDK types to the core public API.
