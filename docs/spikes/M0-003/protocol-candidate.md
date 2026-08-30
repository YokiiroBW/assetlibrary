# M0-003 AssetLink v1 Spike

## Outcome

The candidate selects JSON over HTTPS as the browser baseline. Control uses
`application/assetlink+json;v=1`, events use streamed NDJSON, and file content
uses streamed `application/octet-stream`. Chromium clients can consume these
with `fetch`, `ReadableStream`, `AbortController`, byte ranges, and ordinary
headers. No 100 GB file body enters JSON or requires whole-file buffering.

The schemas cover handshake, control/result/cancel/error, event replay,
resumable upload, ranged download, and explicit verification evidence. Storage,
authorization, locking, conflict policy, snapshot construction, and file writes
remain outside this transport contract.

## Decisions

- Version negotiation chooses the highest exact entry in both advertised
  support lists. No exact match returns `unsupported_version`; an implementation
  never guesses compatibility from a shared major alone.
- Enabled capabilities are the exact intersection of both offers. Unknown
  capability and enum-like strings are preserved for forward compatibility.
- Primary and secondary endpoints must return the same stable `server_id`.
  Clients stop rather than silently accepting a different logical server.
- Every uint64 wire value is a canonical decimal string. The schema format plus
  executable oracle enforces the full `0..18446744073709551615` range.
- Request correlation, cancellation, retry hints, idempotency identities,
  opaque cursors, cursor expiry, inclusive byte ranges, chunk hashes, final
  reread/hash evidence, and readable-destination evidence are explicit.
- Generated SDKs transport DTOs and preserve unknowns. They do not duplicate
  core authorization, storage, synchronization, or transfer state machines.

## Compatibility matrix

| Client offer | Server support | Result |
|---|---|---|
| `1.0` | `1.0`, `1.1` | select `1.0` |
| `1.2`, `1.1` | `1.0`, `1.1` | select `1.1`; preserve optional `1.2` data |
| `1.1` | `1.2` | fail `unsupported_version`; do not guess |
| `1.x` | `2.x` only | fail `unsupported_version` |
| known plus unknown capabilities | known subset | enable only the intersection; retain unknown names |
| secondary endpoint, same `server_id` | prior accepted identity | allow failover |
| secondary endpoint, different `server_id` | prior accepted identity | stop and require user/core resolution |

## Executable evidence

Twenty-one standard-library tests validate all 20 envelope schemas plus the common
definition schema using fixed fixtures. They recursively resolve relative and
internal references, enforce
Draft 2020-12 declarations and unique stable IDs, and exercise:

- old and newer minor-version offers, exact selection, unknown fields, and
  unknown capabilities;
- request/result correlation, retry metadata, failover identity, disconnected
  replay, cursor expiry, nested event validation, and snapshot resync;
- canonical uint64 boundaries including 100 GB, inclusive range bounds,
  64 MiB chunk limits, duplicate-versus-conflicting chunks, ordered received
  ranges, and final reread/length/hash/readability evidence;
- rejection of the original incomplete handshake shape and invalid canonical
  offsets, demonstrating that the checks detect the targeted regressions.

The oracle is intentionally limited to every JSON Schema keyword used by this
candidate; it is not presented as a general JSON Schema implementation.

## Remaining validation and risks

Real Chromium streaming, HTTP/2 framing, proxy behavior, network interruption,
process crash, and durable storage recovery were not executed because M0-003
contains no server or production client. Generated .NET, TypeScript, and Kotlin
SDKs also remain future work. Those gates belong to the implementation/freeze
task; this spike only makes their wire inputs and expected outcomes testable.
