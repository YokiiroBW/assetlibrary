# AssetLink v1 candidate

This directory is the normative wire-contract source for official clients. The
M0 candidate uses browser-native HTTPS: JSON for request/response control,
newline-delimited JSON (NDJSON) for events, and streamed octet bytes for asset
content. File bytes never appear in JSON.

## Transport map

| Flow | Method and path | Media type and headers | Envelope schema | Limit and recovery |
|---|---|---|---|---|
| Handshake | `POST /assetlink/v1/handshake` | `application/assetlink+json;v=1` | `handshake`, `handshake-response`, `error` | 1 MiB; select the highest exact common version |
| Control | `POST /assetlink/v1/control` | JSON, `X-Request-Id`, `Idempotency-Key` when present | `control`, `control-result`, `error` | 30 s default; retry only when the error says so |
| Cancel | `POST /assetlink/v1/control/cancel` | JSON, `X-Request-Id` | `control-cancel`, `control-result`, `error` | Idempotent acknowledgement; original request terminates separately |
| Events | `GET /assetlink/v1/events` | `application/x-ndjson`, `Last-Event-Cursor` | one `event` per non-empty line | heartbeat within 30 s; reconnect and replay |
| Replay | `POST /assetlink/v1/events/replay` | AssetLink JSON | `replay-request`, `replay-result`, `error` | at most 1,000 events; expired cursor requires snapshot resync |
| Download | `GET /assetlink/v1/assets/{id}/content` | `application/octet-stream`, `Range` | `download-range`, `download-result`, `error` | at most 64 MiB per range; `206` plus `Content-Range` |
| Upload create | `POST /assetlink/v1/uploads` | AssetLink JSON, `Idempotency-Key` | `upload-create`, `upload-created`, `error` | canonical uint64 length; retryable |
| Upload chunk | `PUT /assetlink/v1/uploads/{id}/chunks/{offset}` | octet stream, size and SHA-256 headers | `upload-chunk`, `upload-chunk-result`, `error` | at most 64 MiB; identical retry is idempotent |
| Upload status | `GET /assetlink/v1/uploads/{id}` | AssetLink JSON | `upload-status`, `error` | inclusive received ranges allow crash resume |
| Upload complete | `POST /assetlink/v1/uploads/{id}/complete` | AssetLink JSON, `Idempotency-Key` | `upload-complete`, `upload-complete-result`, `error` | reread length, readability, and final SHA-256 |
| Upload cancel | `DELETE /assetlink/v1/uploads/{id}` | AssetLink JSON | `upload-cancel`, `upload-cancel-result`, `error` | idempotent; source remains preserved |

HTTP success status is `200` unless the flow specifies `206`. A malformed or
unsupported envelope is `400`; an unsupported negotiated version is `426` with
`unsupported_version`; an idempotency or chunk conflict is `409`; an expired
cursor is `410`; and an invalid or unsatisfiable byte range is `416`. Errors use
`error.schema.json` even when the transport status is non-success.

## Compatibility and identity

Versions are explicit `major.minor` strings. Both endpoints advertise complete
support lists, and the server selects the numerically highest exact
intersection. An empty intersection returns `unsupported_version`, including
when both sides mention the same major but no shared minor. Minor evolution is
forward compatible through optional fields, not by silently selecting an
unadvertised version.

The response capability list is the intersection of client and server offers;
unknown capability strings are retained but never treated as enabled unless
both endpoints advertise them. Official clients preserve unknown optional
fields, capability names, client kinds, endpoint roles, event types, operations,
and error codes. Unknown `message_type` values go to an explicit unknown-message
branch and are never interpreted as a known command.

`server_id` is stable across primary and secondary endpoints for one logical
server. A client switching endpoints must compare it with the previously
accepted identity and stop on mismatch. Endpoint role alone never establishes
identity.

## Requests, cancellation, and idempotency

`request_id` correlates each response or error with its request. A cancellation
has its own `request_id` and names the original request in `cancel_of`. The
cancel response only acknowledges the cancellation operation; the original
request still reaches a terminal result or `cancelled` error. A local timeout
causes the client to stop waiting and attempt cancellation, but it does not
prove that the server rolled back work.

For control requests, the server scopes an `idempotency_key` to authenticated
caller plus operation. Reusing the key with the same canonical request replays
the stored terminal outcome; reusing it with different content returns
`idempotency_conflict`. Create and complete requests follow the same rule.
For chunks, `(transfer_id, offset)` is the idempotency identity: the same size
and SHA-256 returns `duplicate: true`; different bytes or hash conflict.

Clients retry only when `error.retryable` is true, honor `retry_after_ms` when
present, and reuse both request and idempotency identities for the same logical
attempt. They must not retry non-idempotent work under a new key automatically.

## Events and recovery

Cursors and IDs are opaque strings. An NDJSON consumer ignores empty heartbeat
lines, commits a cursor only after processing that event, reconnects with the
last committed cursor, and calls replay before accepting newer live events.
Replay results are ordered, contain fully validated event envelopes, and expose
the next cursor. `gap: true` or a `cursor_expired` error prevents incremental
continuation. The latter carries `details.action = "full-resync"` and a non-empty
`snapshot_token`; the client rebuilds from that snapshot before reopening the
live stream.

## Bytes and verification

All lengths, offsets, sequence numbers, and inclusive range endpoints are
canonical decimal strings in `0..18446744073709551615`, with no leading zero.
The custom JSON Schema format requires the upper-bound check in consumers.
`chunk_size` remains a JSON integer because it is bounded to 64 MiB.

Range start and end are zero-based and inclusive, so a valid range satisfies
`0 <= start <= end < length`. Upload status ranges are sorted, non-overlapping,
and bounded by the declared length. Completion succeeds only after the server
rereads the persisted destination, confirms it is readable, verifies the exact
declared length, and recomputes the final SHA-256. A success status alone is not
verification evidence.

Authorization, path policy, storage writes, conflict policy, locking, audit,
and transfer state machines remain core responsibilities rather than wire or
SDK business logic.
