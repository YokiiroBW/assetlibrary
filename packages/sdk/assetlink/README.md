# AssetLink SDK consumer boundary

Generated SDKs consume `contracts/assetlink/`; this package contains no client
or server business logic. .NET uses checked `ulong`, TypeScript uses `bigint`
or a decimal-string boundary, and Kotlin uses checked `ULong`/`Long` for all
length, offset, sequence and range values. Unknown fields and enum strings are
preserved through an unknown branch. Unsupported major versions fail closed.
