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
