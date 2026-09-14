# AssetLink v1 read-only trial application contract

Status: coordinator-approved for V01-015..018 implementation (2026-09-07); ADR-0014.
This is additive. Existing envelopes and libraries.list/entries.browse/assets.search bodies remain unchanged. Generated SDKs preserve open operations/bodies; consumers never hand-edit generated sources.

V01-024..026 adds optional category/query controls and authorized detail operations under [web-interaction-v1.md](web-interaction-v1.md) and ADR-0016. Existing requests without these options keep their original defaults.

## Authentication transport

All endpoints are same-origin HTTPS and no-store. No CORS. POST requires one exact configured Origin and a matching Host. A missing/null/multiple/wrong Origin is rejected. All authenticated control/management requests also send `X-AssetLibrary-CSRF` from memory.

- `POST /assetlink/v1/auth/login`: JSON `{account_name,password}`. A successful 200 sets a protected `__Host-AssetLibrary-Session` Cookie and returns Session. Password-manager/paste support remains available. Invalid credentials return generic 401; bounded rate/concurrency rejection 429 plus Retry-After; dependency failure 503.
- `GET /assetlink/v1/auth/session`: 200 Session, or 401 with expired/invalid Cookie removal. Reject conflicting Origin and cross-site Fetch Metadata. Database outage is 503 and is not represented as logout.
- `POST /assetlink/v1/auth/logout`: session-bound CSRF required; success 204 revokes database session and clears Cookie. A failed request must not be presented as a confirmed server logout.

Session fields: `authenticated:true`, `principal_id` (opaque string), `display_name`, `is_system_administrator` (boolean), `csrf_token` (opaque string), `absolute_expires_at` (UTC ISO timestamp). Raw session tokens and secrets are never returned in JSON. Errors have `{code,message}` with safe user-facing messages. Cookie contents/CSRF cannot enter localStorage/sessionStorage, logs or URL parameters.

The frontend revalidates on focus and uses native BroadcastChannel to broadcast only a session-change event; principal or session generation changes discard the previous workspace. An event is a refresh hint, never authorization.

## Read-only trial management

Use `POST /assetlink/v1/control`, existing AssetLink control/error envelopes and request correlation. JSON request size/deadline remain bounded. Management is current-system-administrator only; ordinary reads use the existing per-library permission policy. The Web role flag only changes presentation, never grants access.

| operation | request body | successful body |
|---|---|---|
| `storage_sources.list` | `{}` | `{sources:[{source_key,display_name}]}` |
| `libraries.register` | `{source_key,display_name,root_path}` | `{library_id}` |
| `library_scans.get` | `{library_id}` | `{library_id,scan:ScanSummary|null}` |
| `library_scans.start` | `{library_id}` | `{library_id,scan:ScanSummary}` |
| `library_scans.cancel` | `{library_id,task_id}` | `{library_id,scan:ScanSummary}` |

Register/start/cancel require a UUID `idempotency_key` in the envelope, scoped to authenticated caller and operation. Repeating a logical retry uses the same key and body; conflicting reuse is 409 `idempotency_conflict`. Starting a durable task is a short acceptance request; disconnecting that request does not cancel the task. Cancelling is a separate durable operation. Active same-library scans are not duplicated. Completed initial snapshots reject another initial start with 409 `already_indexed`; failed/cancelled attempts can start a new request.

ScanSummary: `task_id`, `scan_id` (nullable before run), `state` (`queued|leased|succeeded|failed|cancelled`), `cancellation_requested`, `observed_entries`, `committed_entries`, `started_at`/`finished_at` (UTC string|null), `failure_code` (string|null), `can_cancel`, `can_retry`. Counts are non-negative safe integers and represent actual progress; no fabricated percent/total. The UI may display indeterminate progress. Failure/cancellation/offline is not an empty successful index.

Sources are deployment-owned allowlist entries with stable IDs and path comparison; clients cannot supply storage_source_id, comparison mode or arbitrary source definitions. The operator configures allowed roots, and the administrator enters a root by text; no arbitrary server directory-enumeration API exists. Plain read projections/errors never expose absolute paths, tokens, raw task payloads or database details. Registration alone does not start scanning; the UI offers an explicit first-scan action.

Authorization: 401 unauthenticated, 403 forbidden management/CSRF/Origin, 404 absent-or-invisible library, 409 overlap/idempotency/already-indexed conflict, 400 invalid request, 503 unavailable source/service, 504 bounded deadline. Permission loss clears stale browser items/cursors/details; transient unavailable responses preserve an explicitly marked last snapshot. Unavailable library status must not be converted into missing physical files.

## Local operator and worker boundaries

Administrator bootstrap/recovery and authorization-key rotation are local operator operations in the same Host, never anonymous HTTP endpoints. Secrets use protected files or bounded stdin, not CLI arguments. Existing risk screening and recovery CAS/replay rules remain authoritative.

The internal `--read-only-worker probe|scan` transport is process-local and not an AssetLink public network feature. It starts before network/database/secret configuration, receives a bounded root request through stdin and returns bounded NDJSON with a mandatory terminal frame. It cannot enable asset writes or Provider execution.

## TS063 service read authentication appendix

Approved for local implementation by the coordination decision `TS-063-service-read.md` (2026-09-14). The browser transport above remains unchanged. The following explicit service branch is additive and disabled by default (`service_read_enabled:false`). `service_read_maximum_lifetime_days` defaults to365 and accepts30..365; issued credentials default to30 days and may request a shorter lifetime.

A service sends one `Authorization: Bearer <opaque-token>` to `POST /assetlink/v1/control` over HTTPS with the exact configured authority. Any Authorization header selects this branch; it cannot fall back to Cookie authentication. Cookie, Origin, every Sec-Fetch-* header and X-AssetLibrary-CSRF are forbidden, including empty supplied headers. Wrong authority/path/method, insecure transport, disabled feature and mixed browser authentication are403; malformed/unknown/expired/revoked credentials or disabled subjects are401. Missing Authorization uses the unchanged browser trust path (POST without Origin is403). No CORS or service token in a browser.

Only `libraries.list`, `libraries.get`, `entries.browse`, `entries.get` and `assets.search` reach the existing read protocol. Other operations return403 and never reach the management dispatcher. Existing bodies/results, generated SDKs, cancellation, body64KiB, page maximum100 and Host total5-second budget remain unchanged. A request parsed as a valid control message preserves request_id; failures before parsing may use unknown. The proposed Platform1MiB response budget remains consumer-side and requires later two-product acceptance.

Service principals are independent, non-administrative and have no local login. Every request reads current credential/subject state from PostgreSQL; each page's existing authorized query rechecks current library_permission. There is no token ACL replica or cached authorization fallback. An unavailable authentication database is503. Revoked libraries are absent from lists and unscoped search; scoped detail/browse/search return the existing absent-or-invisible404. New queries after committed revocation cannot access that library; an already running repeatable-read snapshot may finish and delivered bytes cannot be recalled.

Local `--trial-operator <private-configuration> service-*` actions accept bounded stdin JSON only; no remote grant/credential endpoint exists. They require the protected local operator key and a distinct service action/version/purpose-bound proof covering every request field; bootstrap/recovery proofs are rejected. Operator documentation and retry rules: `docs/integrations/tianshu/service-read-operator.md`.
