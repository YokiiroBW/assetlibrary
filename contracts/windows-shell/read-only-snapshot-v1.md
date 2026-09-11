# Explorer test-only snapshot IPC v1

Status: frozen by V03-005 on 2026-09-12 under ADR-0018. This local projection is not AssetLink and does not enable production Explorer release gates. Single contract owner: V03-005.

## Scope and ownership

Replace the V03-006 synthetic namespace contents with authorized Core library pages and physical directory pages. AssetHost is a separate C#/.NET process reusing `ClientTransport`, `ReadOnlyClient`, `ServerProfile` and the generated SDK. C++ test-only Shell uses native DefView and bounded local IPC only. No new framework/dependency, HTTP endpoint, database access, credential persistence, file opening, preview, transfer or write operation is introduced. Category grouping and full properties remain subsequent client work; this controlled slice shows the authorized library list directly.

Host owns authentication, HTTPS, Core queries and page cursor/location mapping. Shell owns PIDLs, native navigation, display names/types and current-page comparison. Names are display text, never node identity. Library/directory identity is an opaque random token mapped to the existing Core location; pagination is an explicitly labelled navigation item, never a physical folder. Reparse directories cannot be navigated.

## Endpoint and trust boundary

Byte-mode local named pipe `AssetLibrary.ExplorerProof.v1.<current-user-SID>.<Windows-session-id>`, opened by Shell only as `\\.\pipe\...`. Host must reserve the first instance and restrict access to the actual TokenUser SID (not the token owner group); reject remote clients and clients from other sessions. Shell must verify the connected server process's TokenUser SID and session before accepting a response; a predictable endpoint name alone cannot authenticate its owner. Do not rely solely on package identity or administrator membership. The client uses SECURITY_SQOS_PRESENT | SECURITY_IDENTIFICATION to prevent an endpoint from impersonating it. No credentials, server URL, absolute physical path, Core IDs, raw exception or server error text crosses the pipe. Same-user processes are not treated as mutually isolated security principals; no privileged operation is exposed.

All integers are unsigned little-endian. GUID bytes use .NET Guid.ToByteArray / native GUID memory order. One request and response per connection. Reject unknown versions/types, malformed UTF-16, embedded NUL/control characters, duplicate/zero item tokens, trailing data inside the declared payload and lengths beyond limits before allocation/use. Consume exactly the declared frame and close after the exchange; never wait for EOF or for hypothetical later bytes to validate framing. An invalid packet is a connection/protocol error, never an empty successful library.

Header (16 bytes): magic u32 `0x31534C41` (ALS1), version u16=1, type u16 (1=request, 2=response), payload length u32, request-id u32 (nonzero; response must match). Total payload at most 65536 bytes.

Request payload (32 bytes): host epoch GUID (16), node GUID (16). Both zero means root. Non-root requires the current epoch and a known nonzero node. Host restart/identity loss changes epoch and invalidates every previous token. Old nodes return Expired; never resolve by name.

Response payload: status u32, current host epoch GUID (16), item count u32, then records. Status: 0 Ready, 1 Loading, 2 Unavailable, 3 AccessDenied, 4 Expired, 5 InvalidResponse, 6 Busy. Non-Ready has zero records. Ready may have zero records (genuine empty page), at most 101 records (100 Core entries + one next-page navigation). Host epoch must be nonzero.

Record: node GUID (16), kind u16, name length u16 in UTF-16 code units, then the exact UTF-16LE display name without terminator. Name length 1..255. Kind: 1 Library, 2 Directory, 3 File, 4 Reparse (not navigable), 5 NextPage. Only 1/2/5 are navigable. No write/drop/rename/delete/paste capabilities. PIDLs carry epoch, node, kind and validated display text, never Core paths. Stale names from an already displayed PIDL are not proof of current permission or existence.

## Bounded lifecycle

Shell query has one monotonic total foreground waiting budget of 150 ms including connect/write/header/body. CancelIoEx only requests cancellation: overlapped storage/handles and the DLL lifetime must remain owned until actual completion, with at most 4 retained operations and Busy when capacity is exhausted. Any deferred cleanup cannot extend the foreground wait with an infinite wait; safely completed storage can be released immediately. No network fallback, busy loop or per-item IPC. EnumObjects queries one page; display/attribute/compare calls use the enumerated PIDL. Missing Host, loading, busy and protocol failures produce a non-navigable fixed status item with an F5 retry instruction, not a fake empty library. No fabricated success fallback.

Host returns the current in-memory result immediately and schedules Core work separately. It never waits for HTTPS on the pipe handler. At most 4 live pipe clients, 2 concurrent Core reads, 64 retained page snapshots and 8192 location/item tokens; refuse or expire old locations when bounded capacity is reached. Use existing 8-second HTTP cancellation, at most 5-second successful snapshot freshness, and no stale-data fallback after expiry. Refresh after expiry returns Loading while requerying. Coalesce requests for the same node. Failed queries can retry after a bounded 1-second backoff. 401/403/404 and session expiry cancel pending work, clear all cached identity data and change epoch; late responses cannot repopulate it. No automatic credential retry. Restart/reconnect requires the root to be reopened. Stop cancels and awaits owned work with an explicit deadline and logs only operation/status/timing.

Core HTTP 410 (`cursor_expired`) maps to Expired and retires the affected pagination token, or resets the bounded location epoch and requests reopening the root. It must not repeatedly retry the same invalid cursor as Unavailable.

The first controlled slice uses native F5/reopen to request fresh snapshots; it does not claim push invalidation of names already painted by Explorer. Authorization is rechecked by Core on each page query; the limited display freshness and manual refresh are explicit acceptance limitations pending G2/G3 lifecycle work.

## Integration entry and acceptance

AssetHost's controlled executable takes only a private profile **file path**, optional bounded lifetime and stop-file path. Reuse the native-client fixture's origin/certificate_sha256/account_name/password fields. Never log profile contents or put credentials in arguments. Validate size/schema, use memory-only session, best-effort bounded server logout on normal stop, and distinguish logout failure from local clear. Production login/settings/startup/installer remain pending.

Acceptance: C# encode/decode and state tests; independent C++ byte vectors/malformed bounds/deadline tests; real named-pipe interoperability; production HTTPS Core + isolated PostgreSQL fixture for authorized libraries, nested physical navigation, >100 pagination, inaccessible account and restart/absent Host. Verify actual Explorer view and source-file hashes using the existing native execution route, owner registration guard and new owned window. Do not restart the user's Explorer. A COM probe alone is not actual Explorer acceptance.

Implementation ownership: V03-006 owns `tests/windows-shell/**` only for this slice; V03-010 owns `apps/windows-client/AssetHost/**`, new Host tests and Windows solution/test-project wiring; V03-005 owns shared contract and integration review/acceptance. Each uses its own worktree. No concurrent edits to existing Core adapter without a specific root handoff.

References: [named pipe security](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights), [CreateNamedPipe flags](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createnamedpipew), [CurrentUserOnly token-owner issue](https://github.com/dotnet/runtime/issues/123903). These inform local transport checks; existing AssetLink remains the business contract.
