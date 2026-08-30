# AssetLink v1 candidate

Normative wire source for official clients. Browser baseline: HTTPS JSON
(`application/assetlink+json;v=1`) for control and NDJSON events, plus streaming
`application/octet-stream` for bytes. JSON never contains file bytes.

## Transport map

| Flow | Method/path | Media/headers | Schema | Limits and recovery |
|---|---|---|---|---|
| Handshake | POST `/assetlink/v1/handshake` | JSON, Content-Type/Accept | handshake, handshake-response | 1 MB; negotiate highest intersecting version |
| Control | POST `/assetlink/v1/control` | JSON, X-Request-Id, Idempotency-Key | control, error | 30 s default; retry only when retryable |
| Cancel | POST `/assetlink/v1/control/cancel` | JSON, request ID | control | idempotent; cancelled terminal |
| Events/heartbeat | GET `/assetlink/v1/events` | NDJSON, Last-Event-Cursor | event | 30 s heartbeat; reconnect then replay |
| Replay | POST `/assetlink/v1/events/replay` | JSON | replay | max 1,000; expired cursor requires snapshot |
| Download | GET `/assetlink/v1/assets/{id}/content` | octet-stream, Range | download-range/result | 64 MiB window; 206 + Content-Range |
| Upload create | POST `/assetlink/v1/uploads` | JSON, Idempotency-Key | upload-create | canonical uint64 length; retryable |
| Upload chunk | PUT `/assetlink/v1/uploads/{id}/chunks/{offset}` | octet-stream, chunk hash/size | upload-chunk | max 64 MiB; same hash retry idempotent |
| Upload status | GET `/assetlink/v1/uploads/{id}` | JSON | upload-status | ranges support crash resume |
| Upload complete | POST `/assetlink/v1/uploads/{id}/complete` | JSON, Idempotency-Key | upload-complete | reread, readable, verified and final hash required |
| Upload cancel | DELETE `/assetlink/v1/uploads/{id}` | JSON | upload-cancel | idempotent; source preserved |

Every envelope accepts unknown optional fields. IDs/cursors are opaque strings.
Version offers allow multiple major/minor strings; only disjoint majors fail
with `unsupported_version`. Unknown capabilities, client kinds, endpoint roles
and error codes are preserved as strings by SDKs.

All sizes, offsets, sequences and ranges are canonical decimal strings in
`0..18446744073709551615` (no leading zero), preventing TypeScript precision
loss. .NET uses checked `ulong`, TypeScript `bigint`/decimal string, and Kotlin
checked `ULong`/`Long`. Business rules, auth, storage and state machines remain
in the core.
