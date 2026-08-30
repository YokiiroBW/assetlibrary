# M0-003 AssetLink v1 Spike

JSON-over-HTTPS is the browser baseline. Control and event messages use
`application/assetlink+json;v=1`; events are newline-delimited and bytes use
`application/octet-stream`. Browser clients can use `fetch`, `ReadableStream`,
`AbortController`, Range and idempotency headers. JSON never carries file bytes.

The schemas and standard-library tests validate version/capability negotiation,
same-`server_id` failover, opaque cursor replay, idempotency/error fields and
100 GB 64-bit transfer metadata. Storage, authorization, conflict policy and
snapshot rebuilding remain core responsibilities.

## Decisions, compatibility, evidence

Decimal strings are mandatory for every uint64 wire value. The server chooses
the highest minor version within a compatible major; disjoint majors return
`unsupported_version`. Failover compares the new identity to expected
`server_id`. .NET uses checked `ulong`, TypeScript `bigint`/decimal string,
and Kotlin checked `ULong`/`Long`; unknown fields and enum strings are retained.

Fourteen named standard-library tests load every schema and fixture, recurse
through relative and internal refs, and exercise boundaries, failover, replay,
chunk bounds, final evidence and Range descriptors. Real Chromium, HTTP/2,
network interruption and crash-storage tests were not run; these are explicit
follow-up risks because this task intentionally has no server or client.
