# AssetLink v1 Web interaction queries

Coordinator-approved for V01-024..026, 2026-09-08, ADR-0016. These are additive operations and optional fields inside the existing open control envelope; generated SDK sources remain unchanged. Authentication, Origin/Host, CSRF, bounds, cancellation, permission filtering and idempotency from read-only-trial-v1.md continue to apply.

## Library navigation and metadata

`Library` responses add `category`: `photos|images|videos|music|projects|documents|characters|general`. Categories describe the administrator's explicit library choice, not a filename/content inference or new physical folder. Existing libraries migrate to `general`; an older response without the field may be displayed as general by the Web. Unknown explicit values are invalid, not silently normalized.

| Operation | Request | Successful body |
| --- | --- | --- |
| `libraries.list` | Existing page fields, optional `category` | Existing `items` and `next_cursor`; category filter applies before pagination |
| `libraries.get` | `{library_id}` | `{library}` |
| `entries.get` | `{library_id,entry_id}` | `{library,entry}` |
| `libraries.register` | Existing fields plus optional `category` (default general) | Existing `{library_id}` |
| `libraries.update_category` | `{library_id,category,expected_category}` | `{library_id,category}` |
| `storage_sources.list` | Existing `{}` | Each existing source adds `default_root_path` from its configured allowed root |

`libraries.get` and `entries.get` are ordinary authorized reads. Absent, non-present or inaccessible indexed entries/libraries return the same 404 `not_found`. They do not perform live file I/O or return content/preview URLs; an offline library can still expose its authorized committed snapshot and availability.

Category updates use current-system-administrator authorization, CSRF and an envelope UUID idempotency key. The gateway revalidates current-administrator authorization on each request; the LibraryStorage application validates the command, CAS and idempotency and stores the result for replay. The actor ID is an audit/idempotency scope, not an administrator credential. Different payload with the same operation key is 409 `idempotency_conflict`. A mismatched `expected_category` is 409 `state_conflict`, requiring refresh rather than overwriting another change. Registration carries category in its operation fingerprint; legacy stored registration payloads have the general default.

Source roots remain administrator-only deployment configuration. The Web can register the selected source root without typing its implementation path, with an explicit advanced subdirectory input; the core still probes/canonicalizes the root and enforces the configured boundary. This does not add an arbitrary server-directory enumeration API.

## Complete-scope directory queries

`entries.browse` retains `library_id`, `parent_relative_path`, `page_size` and optional `cursor`, adding:

| Field | Values/default |
| --- | --- |
| `sort_by` | `name` (default), `modified`, `size` |
| `sort_direction` | `asc` (default), `desc` |
| `kind` | `all` (default), `files`, `directories`; reparse entries belong to the corresponding group |
| `name_filter` | Empty by default; trimmed literal case-insensitive name containment, maximum 200 characters; no wildcard language |
| `anchor_entry_id` | Optional initial-page stable entry ID; cannot accompany `cursor` |

Filtering and sorting happen in the authorized server query before LIMIT. The stable order uses the requested primary key, then case-insensitive name and entry ID. Size NULLs always sort last. Cursor anchors preserve their typed timestamp/size/null bucket and are bound to library, parent, kind, name filter, sort and direction. Changed scope/options or malformed cursors are rejected rather than reused.

An anchor must be visible/present in that same parent and match the filters, otherwise 404. The first returned page starts inclusively at the anchor; subsequent requests use only the returned cursor and the same normal query options. Locating a search result never requires fetching an unbounded number of earlier pages. Existing response shape remains; an optional anchor acknowledgement may be included.

## Search scope

`assets.search` keeps its existing query/page fields and adds `scope=all|library|directory` (default all).

- `all`: omit both library and parent fields; search all authorized libraries.
- `library`: require `library_id`, omit `parent_relative_path`.
- `directory`: require `library_id` and explicit `parent_relative_path`; empty parent means the library root subtree.

The physical subtree and authorized library set constrain the search before matching/ranking and LIMIT. Nonexistent/invisible fixed library is 404; contradictory scopes are 400. Cursors bind normalized query plus scope/library/parent. The response stays `items`/`next_cursor` with each hit's library, entry and hit reason. Search does not infer identity, format category or physical ownership from a name.

## Compatibility and implementation ownership

Omitted optional fields preserve existing default queries. Previously issued v1 protected cursors expire when the typed v2 cursor format is deployed; the client restarts that query from its first page rather than treating an old cursor as a new typed anchor. Three append-only migrations belong respectively to LibraryStorage (0019), AssetIdentity (0020) and GatewayAuth (0021), each with its existing single owner. New v2 read functions keep old callable signatures available; the current Host still requires its exact manifest. A rollback of the upgraded Host therefore requires the corresponding pre-upgrade database/state backup, not merely an old image.

No preview/content delivery, general rescan, asset mutation, provider, account lifecycle UI or full Alpha release capability is activated by this contract.
