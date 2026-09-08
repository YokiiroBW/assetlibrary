package app.assetlibrary.android.protocol

import java.awt.image.BufferedImage
import java.io.ByteArrayOutputStream
import java.util.zip.CRC32
import javax.imageio.ImageIO
import org.junit.Assert.*
import org.junit.Test

internal object TestPng {
    fun image(width: Int, height: Int): ByteArray = ByteArrayOutputStream().also {
        ImageIO.write(BufferedImage(width, height, BufferedImage.TYPE_INT_ARGB), "png", it)
    }.toByteArray()

    fun chunk(type: String, data: ByteArray): ByteArray {
        val bytes = type.toByteArray(Charsets.US_ASCII) + data
        val crc = CRC32().apply { update(bytes) }.value
        fun number(value: Long) = byteArrayOf((value shr 24).toByte(), (value shr 16).toByte(), (value shr 8).toByte(), value.toByte())
        return number(data.size.toLong()) + bytes + number(crc)
    }
}

class ImagePayloadTest {
    @Test fun `accepts correct transparent PNG within variant budget`() {
        assertEquals(512, ImagePayload.parse(TestPng.image(512, 256), ImageVariant.THUMBNAIL).width)
        assertEquals(1600, ImagePayload.parse(TestPng.image(1600, 800), ImageVariant.PREVIEW).width)
    }
    @Test fun `rejects excessive axes bytes and invalid CRC before decode`() {
        assertThrows(ApiFailure::class.java) { ImagePayload.parse(TestPng.image(513, 1), ImageVariant.THUMBNAIL) }
        assertThrows(ApiFailure::class.java) { ImagePayload.parse(ByteArray(2097153), ImageVariant.THUMBNAIL) }
        val corrupt = TestPng.image(1, 1).also { it[20] = 127 }
        assertThrows(ApiFailure::class.java) { ImagePayload.parse(corrupt, ImageVariant.PREVIEW) }
    }
    @Test fun `rejects animation private metadata trailing bytes and truncated chunk`() {
        val png = TestPng.image(1, 1)
        for (type in listOf("acTL", "fcTL", "fdAT", "eXIf", "tEXt", "iTXt", "zTXt")) {
            val changed = png.copyOfRange(0, 33) + TestPng.chunk(type, byteArrayOf(0)) + png.copyOfRange(33, png.size)
            assertThrows(ApiFailure::class.java) { ImagePayload.parse(changed, ImageVariant.THUMBNAIL) }
        }
        assertThrows(ApiFailure::class.java) { ImagePayload.parse(png + byteArrayOf(0), ImageVariant.THUMBNAIL) }
        assertThrows(ApiFailure::class.java) { ImagePayload.parse(png.copyOf(png.size - 4), ImageVariant.THUMBNAIL) }
    }
}
