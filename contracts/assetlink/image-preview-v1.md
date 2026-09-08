# AssetLink v1 derived image preview

Coordinator-frozen wire contract for V03-007/008/009, 2026-09-08, ADR-0019. This adds a binary read endpoint; existing authentication/control envelopes and generated message bodies are unchanged. Engine dependencies, OS isolation and feature activation require their own implementation evidence.

## Request and trust

`GET /assetlink/v1/libraries/{library_id}/entries/{entry_id}/image?variant=thumbnail|preview`

Both IDs are non-empty stable UUIDs. The variant is required and must be one of the two exact values; unknown or duplicate query parameters are rejected. Do not accept physical paths, arbitrary URLs, user-selected dimensions or Provider names. Directory and reparse entries cannot produce preview bytes. Source format is decided from validated content/decoder identification, never just its extension.

Use the existing same-origin HTTPS Host/Origin/Fetch-Metadata checks and secure session Cookie. The authenticated subject comes from the server session, not request fields. This GET does not require a new CSRF token; existing POST requirements remain unchanged. No CORS or credential-bearing URL is added. Every request, including a cache hit, revalidates library/entry access before revealing cache existence or reading bytes. Recheck authorization before committing the successful response.

The endpoint returns only server-derived images, never the original file. No Range, conditional-cache or original-download capability is promised by this endpoint. Clients construct this exact origin-relative route from validated IDs; a server response cannot redirect them to another origin.

## Successful representation

| Variant | Maximum width/height | Maximum encoded response | Maximum decoded pixels |
| --- | --- | --- | --- |
| `thumbnail` | longest edge 512 px | 2 MiB (2097152 bytes) | 262144 |
| `preview` | longest edge 1600 px | 12 MiB (12582912 bytes) | 2560000 |

HTTP 200 contains one valid, finite, non-animated, 8-bit PNG (`Content-Type: image/png`) with a correct Content-Length. Preserve aspect ratio (integer rounding may differ by one pixel) and do not upscale. Apply EXIF orientation before sizing; remove source EXIF/text/GPS metadata and use an sRGB display proxy. Preserve alpha. The initial renderer supports static JPEG, PNG and WebP; animated WebP/APNG and unsupported colour variants return 415 rather than claiming full animation/HDR fidelity.

Responses use `Cache-Control: private, no-store`, `X-Content-Type-Options: nosniff` and `Cross-Origin-Resource-Policy: same-origin`. Do not return source paths, source hashes, signed bearer URLs or source metadata in response headers. No shared browser/disk cache is permitted. A client must bound streaming reads regardless of Content-Length, verify PNG signature/header dimensions and its decode budget, and never render HTML/SVG or a successful JSON error as an image.

## Bounded execution and cancellation

The server request has a 15-second total deadline, including authentication, source acquisition, cache/worker and response preparation. The image client has a 20-second total deadline including body reads and any retries. Existing JSON/control requests retain their existing deadlines; do not globally raise them.

No 202 polling protocol or unlimited background queue is introduced. The server has bounded worker concurrency/admission; excess work returns 429. A client uses at most two concurrent image requests per workspace and admits only currently visible thumbnails or the explicitly opened preview. At most two retries of 429 are allowed, respecting Retry-After and the same 20-second total budget. Other failures do not loop automatically. Leaving the viewport, closing the preview, navigation or identity changes cancel obsolete work. Server cancellation terminates or safely detaches only bounded derived work, never mutates originals.

## Errors

Non-200 responses use safe JSON `{code,message}` consistent with the existing authentication transport. Clients must act on HTTP 401/403/404 before reading a potentially malformed or stalled error body. Do not display raw exception paths or decoder output.

| HTTP | Code | Meaning/action |
| --- | --- | --- |
| 400 | `invalid_request` | malformed IDs/variant/query; do not retry unchanged |
| 401 | `unauthenticated` | expired/absent session; clear all identity-scoped images |
| 403 | existing trust/access rejection code | rejected origin/trust/access; clear affected images and revalidate |
| 404 | `not_found` | absent or invisible library/entry, indistinguishable; clear affected images |
| 409 | `source_changed` | indexed/staged source no longer agrees; clear derivative and retain honest L0 fallback |
| 415 | `preview_unsupported` | unsupported content, directory or reparse source; retain L0 |
| 422 | `preview_invalid` | corrupt image; retain L0 |
| 422 | `preview_limit_exceeded` | byte/pixel/resource limit; retain L0 |
| 429 | `preview_busy` | no bounded capacity; `Retry-After: 1` |
| 503 | `preview_unavailable` | offline source, disabled engine or unavailable worker; no false empty/success state |
| 504 | `preview_timeout` | request/worker deadline exceeded; bounded failure |

Unknown error codes remain errors with a safe generic fallback. Never treat them as permission or success. Not-yet-deployed endpoints returning 404 still degrade to L0; the client must not substitute original-file retrieval.

## Source and cache semantics

The server uses its authorized indexed entry and LibraryStorage's published root query, then performs a physically bounded, no-follow read. A stable, bounded input snapshot is copied/read with strong hashing before decoding; sources that disappear, are replaced, escape the root or change during acquisition fail closed. Decoder inputs contain only the accepted snapshot and fixed profile. Source originals must retain hash/mtime.

Derived cache identity includes the strong content hash, decoder/transform version and profile. A cache key, entry ID or possession of old bytes never grants access. Cache hits still verify source/permission freshness; source changes cannot silently produce a derivative associated with another version. Initial source admission ceilings are 32 MiB, 40 million pixels and 16384 pixels per axis, subject to stricter validated platform limits. Disk/memory/concurrency limits, eviction, atomic publication and crash recovery must be explicit in the engine implementation.

Client image memory belongs to one workspace/session generation and is bounded; no cross-account/global persistent image cache. Dispose Object URLs/bitmaps on eviction, close, identity loss and obsolete requests. Hide/clear sensitive images when a page/activity leaves the foreground and reacquire after session/library revalidation. Permission is also revalidated on refresh and subsequent requests. This contract does not introduce push revocation or promise remote erasure of bytes already legitimately displayed.

## Ownership and rollout

V03-005 owns this wire contract, public-port decisions, generated SDK provenance, root dependency/CI changes and unified integration. V03-007 owns approved core/worker implementation; V03-008 and V03-009 own native consumers. No database migration is implied. Any required shared-port or migration change needs a coordinator-approved proposal.

Do not enable decoding where the approved isolation/safety requirements cannot be demonstrated. A disabled/unavailable implementation returns 503 and keeps L0 available. This contract alone closes no Provider, Explorer, file-write or full-release gate.
