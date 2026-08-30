# Provider isolation candidate (M0-008)

`provider-manifest.schema.json` and `provider-rpc.schema.json` are a versioned
candidate boundary for an isolated Provider process. They are not a production
security boundary until M0-009 accepts an enforcement design.

## Manifest invariants

- `manifest_version` and `api_versions` are explicit; unsupported API versions
  fail closed. Unknown optional fields and capability strings are retained.
- `permissions.original_write` is permanently `false`; `input_tokens_only` is
  permanently `true`. A Provider receives supervisor-issued opaque tokens and
  never an arbitrary host path.
- Network is denied by default. `network: true` is only a request for a named,
  supervisor-approved profile and is not permission to self-enable networking.
- Results are metadata, suggestions, or supervisor-owned derived artifacts; no
  result authorizes a physical operation plan.

## RPC framing

The Spike uses a 4-byte big-endian unsigned length followed by one UTF-8 JSON
object. Frames above the negotiated `max_*_bytes` are rejected before payload
allocation; partial frames are bounded by an absolute request deadline. RPC
messages carry `request_id`; `cancel` carries `cancel_of`; future fields are
preserved by the adapter.

The Python supervisor and fixtures under `tests/spikes/provider-sandbox/` are
test-only. They are intentionally not imported by production services or
Providers.
