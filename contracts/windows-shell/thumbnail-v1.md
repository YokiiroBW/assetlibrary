# Explorer derived thumbnail IPC v1

Frozen by V03-005, 2026-09-13, ADR-0021. This is a separate read-only presentation adapter. Snapshot v1, opaque PIDL identity and Core image-preview-v1 HTTP remain unchanged.

Endpoint `AssetLibrary.ExplorerThumbnail.v1.<TokenUserSID>.<WindowsSessionId>`. Same actual-token-user DACL, first instance, remote rejection, same-session verification and Identification SQOS as existing local pipes. Client verifies the held server process identity. Never pass source paths/URLs, credentials, Core UUIDs or raw error bodies to Shell. Host alone maps the already authorized epoch/node to Core library/entry IDs.

## Frame

All fields unsigned little endian; GUID .NET/native memory order. Header16: magic u32 `0x31474C41` (ALG1), version u16=1, type u16 (1=request;2=response), payload bytes u32, nonzero request-id u32 (response echoes). Exactly one request and response per pipe connection; consume declared bytes without waiting for EOF. Reject unknown versions/types, oversized/truncated/trailing declared data and bad correlation before publication.

Request payload32: epoch GUID16, node GUID16; both nonzero, node must map to a normal file in the current SnapshotStore epoch. No filename type guessing, arbitrary sizes, variants or codec names. Thumbnail variant is always the existing server-derived `thumbnail` (longest edge512).

Response prefix56:

| Offset | Field |
| --- | --- |
| 0 | status u32: Ready0, Loading1, Unavailable2, AccessDenied3, Expired4, InvalidResponse5, Busy6, Unsupported7 |
| 4 | epoch GUID16 |
| 20 | node GUID16 (echo request node) |
| 36 | width u32 |
| 40 | height u32 |
| 44 | stride u32 |
| 48 | format u32 (Ready=1, otherwise0) |
| 52 | pixel byte count u32 |
| 56 | pixel bytes |

Ready requires response epoch equal request epoch, width/height1..512, width×height≤262144, stride=width×4, length=stride×height≤1048576, total payload=56+length≤1048632. Format1 is top-down 32-bit premultiplied BGRA/sRGB (transparent channels premultiplied). No compressed bytes, padding rows, palette, animation or colour profile. Non-Ready has width/height/stride/format/length all zero and payload exactly56; no prior pixels. Loading is reserved for compatibility and does not justify unbounded polling. Malformed frames are InvalidResponse, never Ready/empty success.

## Asynchrony, cancellation and budgets

The thumbnail connection can remain open while deriving one image; unlike the page snapshot handler it is a bounded asynchronous worker operation. **Never call it from Paint/UIA/keyboard or block a UI thread awaiting it.** The visible set contains all ordinary-file nodes in the bounded current page (at most101). Shell admits at most16 unfinished image requests per view,64 per module, with two in-flight image operations per module; completing one batch admits the remaining visible nodes. Completed success or failure is not automatically retried while its viewport ticket remains current. Retained images remain at most16MiB per view and64MiB across four views. An image rejected by that byte budget must leave an honest unavailable placeholder, never perpetual Loading. Navigation, hidden view, changed viewport, identity invalidation and destruction cancel obsolete work. UI destruction never joins a worker. Retain buffers/handles/module until real I/O completion; stale view generations cannot post image data back into retired surfaces. Render/control state stays local and bounded.

Coordinator clarification after wide-window native acceptance, 2026-09-13: the original16-candidate wording starved items17 onward when all30 test items fitted onscreen without scrolling. Sixteen bounds unfinished request admission, not the entire visible set. Wire bytes, image dimensions and memory budgets are unchanged.

The16MiB view image budget is shared across admitted pixels in the Surface, completion queue and taken UI batches. A plain byte lease follows shared image ownership and is returned only after the last reference is released; navigation/cancellation must not reset the counter while older pixels remain held. Refused admission produces a no-pixel terminal fallback. The two bounded IPC frame buffers and not-yet-admitted decoded PBGRA buffers are separate transient transport storage (each at most1MiB per operation), not an unbounded image cache.

Host has at most4 authenticated pipe clients; initial request frame read500ms. Each image's20-second total limit starts with the request and includes waiting for image/network admission, HTTP20s maximum, 2MiB streaming, at most2 rate-limit retries and decode. Client disconnect cancels corresponding work; monitor it concurrently, reject unexpected request bytes. Successful write has bounded500ms drain/client-close before disconnecting so unread bytes are not discarded. No automatic retries except server429 within the same deadline. C++ background transport outer budget20s; foreground page150ms remains unchanged.

Host uses one simultaneous image network job initially, sharing a total2 HTTP admission limit with foreground JSON. No finished-image disk or Host cross-view cache. Current SnapshotStore token cap8192 is unchanged. HTTP uses existing same-origin Cookie/Origin/pin/no-redirect policy. HTTP401/403 invokes existing identity invalidation before publishing no-pixel failure; 404/409/415/422/503 retain honest per-item fallback and do not revoke the whole session merely because the optional endpoint is absent. 429 budget exhaustion is Busy; connection/timeout failure Unavailable. Old epochs become Expired; cancellation never restores data.

## Decoder boundary

Host validates bounded Content-Length/read count, image/png signature/IHDR, ≤512 dimensions and pixel product,8-bit static PNG and rejects APNG. Remaining byte validation and fixed system PNG/WIC decoding run only in `AssetLibrary.Host.exe --decode-thumbnail` short-lived helper, never in the normal session process or Explorer. Helper IPC is private implementation, tightly length-bounded, contains only encoded/decoded bytes, and may not be exposed as a general path/URL reader. Job kill-on-close, one-process limit,128MiB memory, ≤3s decode wall deadline within image total; cancellations terminate only this owned child. Parent must validate returned dimensions/format/length and child exit before publication. Clear inherited credential environment; no profile loading, secrets or unrelated handle inheritance in helper mode. Production helper errors log only fixed stage/status codes.

## Verification

Independent C# and C++ byte vectors (valid opaque/alpha image, malformed dimensions/stride/length/format/request id/epoch); actual named-pipe partial/timeout/disconnect and reclamation; HTTP auth/error-body stall/redirect/oversize/cancel/404 fallback; WIC invalid/APNG/oversize/timeout/job cleanup and old-epoch decode completion. Real Core generates fixture thumbnails under its unchanged isolation contract; synthetic UI harness pixels alone do not prove server or NAS preview support.
