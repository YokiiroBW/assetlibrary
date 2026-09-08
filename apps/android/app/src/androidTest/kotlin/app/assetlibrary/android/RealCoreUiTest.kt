package app.assetlibrary.android

import android.graphics.Bitmap
import android.os.ParcelFileDescriptor
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.test.platform.app.InstrumentationRegistry
import app.assetlibrary.android.protocol.*
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import java.io.File

/** Run explicitly against the isolated Core/PostgreSQL fixture; no credentials live in source or arguments. */
class RealCoreUiTest {
    @get:Rule val compose = createAndroidComposeRule<MainActivity>()
    @Test fun realHttpsCoreBrowseAndAccountIsolation() {
        val descriptor = InstrumentationRegistry.getInstrumentation().uiAutomation.executeShellCommand(
            "cat /data/local/tmp/assetlibrary-native-connection.json")
        val source = ParcelFileDescriptor.AutoCloseInputStream(descriptor).bufferedReader().use { it.readText() }
        check(source.startsWith("{")) { "The isolated native fixture connection file must be supplied." }
        val config = Json.parseToJsonElement(source).jsonObject
        fun value(name: String) = config.getValue(name).jsonPrimitive.content
        val profile = ServerProfile.parse(value("origin"), value("certificate_sha256"))
        val protocol = AssetLinkClient(profile)
        var sampleName = ""
        val library = runBlocking {
            protocol.signIn(value("account_name"), value("password"))
            val selected = protocol.libraries(null, null).items.single { it.id == value("library_id") }
            val first = protocol.browse(selected, "", null, BrowseOptions())
            assertEquals(100, first.items.size)
            assertNotNull(first.next)
            val second = protocol.browse(selected, "", first.next, BrowseOptions())
            assertTrue(second.items.isNotEmpty())
            assertTrue(first.items.map { it.entry.id }.intersect(second.items.map { it.entry.id }.toSet()).isEmpty())
            val folder = (first.items + second.items).first { it.entry.directory }
            assertTrue(protocol.browse(selected, folder.entry.path, null, BrowseOptions()).items.isNotEmpty())
            val file = first.items.first { !it.entry.directory }
            sampleName = file.entry.name
            assertEquals(file.entry.id, protocol.detail(selected.id, file.entry.id).entry.id)
            assertTrue(protocol.search(file.entry.name, SearchScope("library", selected.id), null).items.isNotEmpty())
            protocol.signOut()
            protocol.signIn(value("invisible_account_name"), value("invisible_account_password"))
            assertTrue(protocol.libraries(null, null).items.isEmpty())
            protocol.signOut()
            selected
        }
        compose.onNodeWithText("HTTPS 服务器地址").performTextReplacement(profile.origin)
        if (compose.onAllNodesWithText("叶证书 SHA256 指纹（可选）").fetchSemanticsNodes().isEmpty()) {
            compose.onNodeWithText("NAS 自签证书设置").performClick()
        }
        compose.onNodeWithText("叶证书 SHA256 指纹（可选）").performScrollTo().performTextReplacement(profile.certificatePin)
        compose.onNodeWithText("账号").performScrollTo().performTextInput(value("account_name"))
        compose.onNodeWithText("口令").performScrollTo().performTextInput(value("password"))
        compose.onNodeWithText("登录", useUnmergedTree = true).performScrollTo().performClick()
        compose.waitUntil(15_000) { compose.onAllNodesWithText(library.name).fetchSemanticsNodes().isNotEmpty() }
        capture("real-core-libraries")
        compose.onAllNodesWithText(library.name).onLast().performClick()
        compose.waitUntil(10_000) { compose.onAllNodesWithText("本页 100 项").fetchSemanticsNodes().isNotEmpty() }
        capture("real-core-browser")
        compose.activityRule.scenario.recreate()
        compose.waitUntil(10_000) { compose.onAllNodesWithText("本页 100 项").fetchSemanticsNodes().isNotEmpty() }
        compose.onNodeWithText("下一页").performClick()
        compose.waitUntil(10_000) { compose.onAllNodesWithText("第 2 页 · 每页最多 100 项").fetchSemanticsNodes().isNotEmpty() }
        compose.onNodeWithText("上一页").performClick()
        compose.waitUntil(10_000) { compose.onAllNodesWithText("本页 100 项").fetchSemanticsNodes().isNotEmpty() }
        val information = compose.onAllNodes(hasContentDescription("文件信息", substring = true)).onFirst()
        information.performClick()
        compose.waitUntil(10_000) { compose.onAllNodesWithText("真实相对路径").fetchSemanticsNodes().isNotEmpty() }
        capture("real-core-detail")
        compose.onNodeWithText("关闭").performClick()
        compose.onNodeWithContentDescription("搜索").performClick()
        compose.onNodeWithText("搜索文件名或路径").performTextInput(sampleName)
        compose.waitUntil(10_000) { compose.onAllNodesWithText(sampleName, useUnmergedTree = true).fetchSemanticsNodes().size >= 2 }
        capture("real-core-search")
        compose.onAllNodesWithText("连接").onLast().performClick()
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
