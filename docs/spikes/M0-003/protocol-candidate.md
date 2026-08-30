# M0-003 AssetLink v1 Spike

JSON-over-HTTPS is the browser baseline. Control and event messages use
`application/assetlink+json;v=1`; events are newline-delimited and bytes use
`application/octet-stream`. Browser clients can use `fetch`, `ReadableStream`,
`AbortController`, Range and idempotency headers. JSON never carries file bytes.

The schemas and standard-library tests validate version/capability negotiation,
same-`server_id` failover, opaque cursor replay, idempotency/error fields and
100 GB 64-bit transfer metadata. Storage, authorization, conflict policy and
snapshot rebuilding remain core responsibilities.
