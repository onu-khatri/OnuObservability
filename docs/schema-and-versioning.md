# Schema and version policy

Descriptors, finite registries, units, histogram boundaries, event IDs, and fallback semantics are compatibility contracts. Registration is startup-only: identical definitions are idempotent, conflicting definitions fail, snapshots are immutable, and runtime emission requires the exact registered operation descriptor.

An additive API or signal advances the minor version. Removal, rename, unit change, meaning change, fallback change, or bounded-value contraction is breaking and advances the major version with migration guidance. Compatible implementation and diagnostic fixes advance the patch version. All four packages use one aligned immutable semantic version.

Unknown runtime outcomes, errors, reasons, methods, protocols, tools, routes, or methods map to `other` / `unknown` or are dropped before signal creation. Public API and schema fixtures must be updated only with a reviewed compatibility decision.
