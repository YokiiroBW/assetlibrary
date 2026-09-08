package app.assetlibrary.android

import android.graphics.Bitmap
import android.graphics.Color
import android.os.ParcelFileDescriptor
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.test.platform.app.InstrumentationRegistry
import app.assetlibrary.android.protocol.*
import app.assetlibrary.android.workspace.WorkspaceImages
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import java.io.File

/** Explicit integration against the coordinator's real Core/PG and synthetic 图片样例 directory. */
class RealCoreImageUiTest {
    @get:Rule val compose = createAndroidComposeRule<MainActivity>()

    @Test fun actualDerivedImagesOrientationAlphaAndAccountIsolation() {
        val descriptor = InstrumentationRegistry.getInstrumentation().uiAutomation.executeShellCommand(
            "cat /data/local/tmp/assetlibrary-native-connection.json")
        val source = ParcelFileDescriptor.AutoCloseInputStream(descriptor).bufferedReader().use { it.readText() }
        check(source.startsWith("{")) { "The isolated native fixture connection file must be supplied." }
        val config = Json.parseToJsonElement(source).jsonObject
        fun value(name: String) = config.getValue(name).jsonPrimitive.content
        val profile = ServerProfile.parse(value("origin"), value("certificate_sha256"))
        val client = AssetLinkClient(profile)
        val library = runBlocking {
            client.signIn(value("account_name"), value("password"))
            val library = client.libraries(null, null).items.single { it.id == value("library_id") }
            suspend fun sample(name: String) = client.search(name, SearchScope("directory", library.id, "图片样例"), null)
                .items.single { it.entry.name == name }
            var jpeg: ByteArray? = null
            for (name in listOf("landscape.jpg", "landscape.png", "landscape.webp", "rotate-six.jpg", "transparent.png", "重复内容.dat")) {
                val row = sample(name)
                val thumbnail = client.image(library.id, row.entry.id, ImageVariant.THUMBNAIL)
                assertTrue(thumbnail.width <= 512 && thumbnail.height <= 512)
                val preview = client.image(library.id, row.entry.id, ImageVariant.PREVIEW)
                assertTrue(preview.width <= 1600 && preview.height <= 1600)
                if (name == "landscape.jpg") jpeg = preview.bytes
                if (name == "重复内容.dat") assertArrayEquals(jpeg, preview.bytes)
                if (name == "rotate-six.jpg") assertTrue(preview.height > preview.width)
                if (name == "transparent.png") {
                    val bitmap = WorkspaceImages.decode(preview)
                    assertTrue(bitmap.hasAlpha())
                    assertTrue(Color.alpha(bitmap.getPixel(0, 0)) < 255)
                }
            }
            for (name in listOf("active.svg", "not-an-image.png", "oversized-header.png", "truncated.jpg")) {
                val row = sample(name)
                try { client.image(library.id, row.entry.id, ImageVariant.PREVIEW); fail("Invalid synthetic image was rendered") }
                catch (error: ApiFailure) { assertTrue(error.status == 415 || error.status == 422) }
            }
            val remembered = sample("landscape.png")
            client.signOut()
            client.signIn(value("invisible_account_name"), value("invisible_account_password"))
            try { client.image(library.id, remembered.entry.id, ImageVariant.THUMBNAIL); fail("Invisible image disclosed") }
            catch (error: ApiFailure) { assertEquals(404, error.status) }
            client.signOut()
            library
        }
        compose.onNodeWithText("HTTPS 服务器地址").performTextReplacement(profile.origin)
        if (compose.onAllNodesWithText("叶证书 SHA256 指纹（可选）").fetchSemanticsNodes().isEmpty())
            compose.onNodeWithText("NAS 自签证书设置").performClick()
        compose.onNodeWithText("叶证书 SHA256 指纹（可选）").performScrollTo().performTextReplacement(profile.certificatePin)
        compose.onNodeWithText("账号").performScrollTo().performTextInput(value("account_name"))
        compose.onNodeWithText("口令").performScrollTo().performTextInput(value("password"))
        compose.onNodeWithText("登录", useUnmergedTree = true).performScrollTo().performClick()
        compose.waitUntil(15_000) { compose.onAllNodesWithText(library.name).fetchSemanticsNodes().isNotEmpty() }
        compose.onAllNodesWithText(library.name).onLast().performClick()
        compose.onNodeWithContentDescription("搜索").performClick()
        compose.onNodeWithText("搜索文件名或路径").performTextInput("landscape.png")
        compose.waitUntil(20_000) { compose.onAllNodesWithContentDescription("landscape.png 缩略图").fetchSemanticsNodes().isNotEmpty() }
        capture("real-image-thumbnails")
        compose.onNodeWithContentDescription("landscape.png 缩略图", useUnmergedTree = true).performClick()
        compose.waitUntil(20_000) { compose.onAllNodesWithContentDescription("landscape.png 图片预览").fetchSemanticsNodes().isNotEmpty() }
        compose.onNodeWithText("放大").performClick()
        compose.onNodeWithText("150%").assertIsDisplayed()
        compose.onNodeWithText("复位").performClick()
        capture("real-image-preview")
        compose.onNodeWithText("关闭预览").performClick()
        compose.onNodeWithText("搜索文件名或路径").assertIsDisplayed()
        // Recreating the actual Activity forces session/library revalidation and fresh image admission.
        compose.activityRule.scenario.recreate()
        compose.waitUntil(20_000) { compose.onAllNodesWithContentDescription("landscape.png 缩略图").fetchSemanticsNodes().isNotEmpty() }
        val connection = compose.onAllNodesWithText("连接").fetchSemanticsNodes()
        if (connection.isNotEmpty()) compose.onAllNodesWithText("连接").onLast().performClick()
        else compose.onNodeWithText("连接与账号").performClick()
        compose.onNodeWithText("退出并清除本机会话").performScrollTo().performClick()
        compose.waitUntil(10_000) { compose.onAllNodesWithText("登录", useUnmergedTree = true).fetchSemanticsNodes().isNotEmpty() }
    }

    private fun capture(name: String) {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val screenshot = File(context.getExternalFilesDir(null), "$name.png")
        screenshot.outputStream().use {
            compose.onRoot().captureToImage().asAndroidBitmap().compress(Bitmap.CompressFormat.PNG, 100, it)
        }
        InstrumentationRegistry.getInstrumentation().uiAutomation.executeShellCommand("cp ${screenshot.absolutePath} /data/local/tmp/$name.png").close()
    }
}
