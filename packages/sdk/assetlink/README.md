# AssetLink SDK consumer boundary

Generated SDKs consume `contracts/assetlink/`; this package contains no client
or server business logic. Generation must keep the JSON wire shape stable and
share one contract source rather than hand-maintaining three implementations.

## Numeric mapping

All AssetLink uint64 values arrive as decimal strings and must be validated as
canonical text before numeric conversion.

| Target | DTO boundary | Checked application conversion |
|---|---|---|
| .NET | `string` | invariant `ulong` parse with overflow rejection |
| TypeScript | `string` | keep as text or convert to `bigint` after range validation; never `number` |
| Kotlin | `String` | checked `ULong` conversion; never narrow to signed `Long` without a bound check |

Writers serialize the canonical decimal form again. Code must reject signs,
leading zeros other than `"0"`, more than 20 digits, and values above
`18446744073709551615` before performing arithmetic.

## Evolution mapping

Generated models retain unknown object properties in an extension-data bag and
preserve unknown capability, client-kind, endpoint-role, event-type, operation,
and error-code strings. Dispatchers have an explicit unknown `message_type`
branch; they do not coerce an unknown envelope into a known command. Unsupported
negotiated versions fail closed, while optional fields in a selected version
remain forward compatible.

The generated layer may provide parsing, serialization, cancellation hooks,
timeouts, and typed success/error envelopes. Authentication, permissions,
paths, hashes, retry policy decisions, transfer state, storage, and file writes
remain in the shared application/core implementation.

## Generated packages

Run `python -B scripts/generate_assetlink_sdks.py` from the repository root to
refresh all targets, or add `--check` to verify committed output without writing.
The generator consumes every file under `contracts/assetlink`, writes a shared
shape catalog plus target source, and records byte hashes in
`generation-manifest.json`. Files carrying an auto-generated header must not be
edited by hand.

| Target | Public surface | Build boundary |
|---|---|---|
| `.NET` | `AssetLinkCodec`, generated message wrappers, `AssetLinkUInt64` | C# 14 / .NET 10; BCL only |
| `TypeScript` | open generated interfaces, parse/encode/classify helpers, checked `bigint` conversion | TypeScript 6.0.3 / Node.js 24 LTS; no runtime npm dependency |
| `Kotlin` | generated `JsonObject` wrappers, parse/encode/classify helpers, checked `ULong` conversion | Kotlin 2.3.20 / JDK 21 / kotlinx serialization JSON 1.11.0 |

The parsers intentionally preserve the complete JSON object. They identify the
wire envelope but do not implement semantic validation, version negotiation,
authorization, retries, network transport, file access, or transfer state.
Those decisions remain with the owning application/core modules.

Each target contains `.contract-source.sha256`. Repository architecture checks
reject a missing or stale stamp, while generator tests reject missing, modified,
or unexpected generated files. The root `generation-manifest.json` records the
single normalized schema shape and every target output digest for deterministic
compatibility inspection; it is generated evidence, not a second contract source.
