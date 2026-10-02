# Versioning

All four packages ship as one aligned semantic-version family.

- Patch: compatible implementation, documentation, or diagnostic correction.
- Minor: additive public API or schema fields with unchanged existing meaning.
- Major: removed/changed public API, renamed signals, changed units, changed bounded-value meaning, or incompatible configuration behavior.

Package versions are immutable. A rejected artifact receives a new version; it is never overwritten. The approved initial prerelease was `0.1.0-alpha.1`; migration cleanup advanced the current replacement candidate to `0.1.0-alpha.9`. Stable promotion requires separate explicit approval.
