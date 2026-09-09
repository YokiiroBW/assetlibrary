package app.assetlibrary.android.protocol

import java.util.zip.CRC32

enum class ImageVariant(val wire: String, val maxEdge: Int, val maxBytes: Int) {
    THUMBNAIL("thumbnail", 512, 2 * 1024 * 1024), PREVIEW("preview", 1600, 12 * 1024 * 1024),
}

/** A bounded, structurally checked server derivative; never a source file. */
class ImagePayload private constructor(val bytes: ByteArray, val width: Int, val height: Int, val variant: ImageVariant) {
    companion object {
        fun parse(bytes: ByteArray, variant: ImageVariant): ImagePayload {
            fun invalid(): Nothing = throw ApiFailure(0, "preview_invalid")
            if (bytes.size > variant.maxBytes) throw ApiFailure(0, "preview_limit_exceeded")
            val signature = byteArrayOf(137.toByte(), 80, 78, 71, 13, 10, 26, 10)
            if (bytes.size < 45 || !bytes.copyOfRange(0, 8).contentEquals(signature)) invalid()
            fun number(offset: Int): Long = (0..3).fold(0L) { value, index -> (value shl 8) or (bytes[offset + index].toLong() and 255) }
            if (number(8) != 13L || bytes.copyOfRange(12, 16).toString(Charsets.US_ASCII) != "IHDR") invalid()
            val width = number(16)
            val height = number(20)
            if (width !in 1..variant.maxEdge.toLong() || height !in 1..variant.maxEdge.toLong() ||
                width * height > variant.maxEdge.toLong() * variant.maxEdge) throw ApiFailure(0, "preview_limit_exceeded")
            if (bytes[24].toInt() != 8 || bytes[25].toInt() !in listOf(0, 2, 3, 4, 6) || bytes[26].toInt() != 0 ||
                bytes[27].toInt() != 0 || bytes[28].toInt() !in 0..1) invalid()
            var offset = 8
            var imageData = false
            var ended = false
            while (offset <= bytes.size - 12) {
                val length = number(offset)
                if (length > bytes.size - offset - 12) invalid()
                val size = length.toInt()
                val type = bytes.copyOfRange(offset + 4, offset + 8).toString(Charsets.US_ASCII)
                // Animation and private source metadata are outside the frozen display-proxy contract.
                if (type in listOf("acTL", "fcTL", "fdAT", "eXIf", "tEXt", "zTXt", "iTXt")) invalid()
                if (type == "IHDR" && offset != 8) invalid()
                val crc = CRC32().apply { update(bytes, offset + 4, size + 4) }.value
                if (crc != number(offset + size + 8)) invalid()
                if (type == "IDAT") imageData = true
                offset += size + 12
                if (type == "IEND") { if (size != 0) invalid(); ended = true; break }
            }
            if (!imageData || !ended || offset != bytes.size) invalid()
            return ImagePayload(bytes, width.toInt(), height.toInt(), variant)
        }
    }
}

internal fun ApiFailure.imageMessage(): String = when {
    status == 401 || status == 403 -> userMessage
    status == 404 -> "图片预览不可用，可查看文件信息"
    code == "source_changed" || status == 409 -> "源文件已变化，请刷新目录。"
    code == "preview_limit_exceeded" || code == "response_too_large" -> "图片超过安全预览上限。"
    code == "preview_unsupported" || status == 415 -> "此文件暂不支持图片预览。"
    code == "preview_invalid" || status == 422 -> "图片损坏或返回内容无效。"
    code == "preview_unavailable" || status == 503 -> "图片预览暂不可用，资源库可能离线。"
    code == "preview_busy" || status == 429 -> "预览繁忙，请稍后重试。"
    code == "preview_timeout" || status == 504 -> "图片加载超时，请重试。"
    else -> userMessage
}
