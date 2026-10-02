# Privacy classification

Allowed telemetry is operational metadata from finite registries: service identity, schema/policy version, registered operation/event/tool/route, HTTP status class, protocol version, duration, bounded counts, finite outcomes, errors, and reasons, plus generated trace/span identifiers.

Never record prompts, bodies, tool arguments/results, record content, credentials, authorization, cookies, arbitrary headers, query strings, raw URLs or paths, client addresses, exception messages/stacks, private filesystem paths, or raw user/session/object identifiers. Rejection and normalization occur before formatting, queueing, or export. Collector deletion rules are defense in depth, not permission for applications to emit sensitive values.

Local JSON uses reviewed categories/events/fields only. Unapproved records are filtered before formatting. Persistent queues and capture directories require explicit retention, access, encryption, incident-response, and secure-deletion approval.
