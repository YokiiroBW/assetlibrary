package app.assetlibrary.assetlink

import kotlinx.serialization.json.Json

public fun main() {
    knownMessageKeepsUnknownFields()
    unknownMessageUsesExplicitBranch()
    uint64BoundariesAreChecked()
    malformedDocumentsFailClosed()
    println("Kotlin AssetLink generated SDK verification passed.")
}

private fun knownMessageKeepsUnknownFields() {
    val source = """
        {
          "message_type":"control.request",
          "request_id":"request-1",
          "operation":"future.operation",
          "body":{},
          "future_field":{"answer":42}
        }
    """.trimIndent()
    val parsed = AssetLinkCodec.parse(source)
    check(parsed is ControlRequestMessage)
    check(parsed.operation == "future.operation")
    check(AssetLinkCodec.classify(parsed.messageType) == AssetLinkMessageKind.CONTROL_REQUEST)
    check(Json.parseToJsonElement(parsed.encode()) == Json.parseToJsonElement(source))
}

private fun unknownMessageUsesExplicitBranch() {
    val source = """{"message_type":"future.message","future_enum":"new-value"}"""
    val parsed = AssetLinkCodec.parse(source)
    check(parsed is UnknownAssetLinkMessage)
    check(AssetLinkCodec.classify(parsed.messageType) == AssetLinkMessageKind.UNKNOWN)
    check(Json.parseToJsonElement(parsed.encode()) == Json.parseToJsonElement(source))
}

private fun uint64BoundariesAreChecked() {
    listOf("0", "1", "18446744073709551615").forEach { value ->
        check(AssetLinkUInt64.format(AssetLinkUInt64.parse(value)) == value)
    }
    listOf("", "+1", "-1", "00", "01", "18446744073709551616", "100000000000000000000").forEach {
        expectFailure { AssetLinkUInt64.parse(it) }
    }
}

private fun malformedDocumentsFailClosed() {
    expectFailure { AssetLinkCodec.parse("[]") }
    expectFailure { AssetLinkCodec.parse("{}") }
    expectFailure { AssetLinkCodec.parse("""{"message_type":42}""") }
}

private inline fun expectFailure(block: () -> Unit) {
    check(runCatching(block).isFailure)
}
