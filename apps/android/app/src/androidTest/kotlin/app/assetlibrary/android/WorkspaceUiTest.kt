package app.assetlibrary.android

import android.graphics.Bitmap
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.v2.createComposeRule
import androidx.test.platform.app.InstrumentationRegistry
import app.assetlibrary.android.protocol.*
import app.assetlibrary.android.ui.WorkspaceApp
import app.assetlibrary.android.ui.WorkspaceTheme
import app.assetlibrary.android.workspace.WorkspaceModel
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test
import java.io.File
import java.time.Instant

class WorkspaceUiTest {
    @get:Rule val compose = createComposeRule()
    private val library = Library("sample", "设计素材", "online", "read_only", "images")
    private val folder = Entry("folder", "sample", "2026", "2026", "directory", null, "2026-09-08T00:00:00Z")
    private val file = Entry("photo", "sample", "2026/山间湖畔.jpg", "山间湖畔.jpg", "file", "12582912", "2026-09-08T00:00:00Z")
    private val fake = object : AssetApi {
        override suspend fun signIn(account: String, password: String) = Session("设计师", false, Instant.now().plusSeconds(3600))
        override suspend fun validateSession() = Session("设计师", false, Instant.now().plusSeconds(3600))
        override suspend fun signOut() = Unit
        override fun clearSession() = Unit
        override suspend fun libraries(cursor: String?, category: String?) = Page(listOf(library), null)
        override suspend fun browse(library: Library, path: String, cursor: String?, options: BrowseOptions, anchor: String?) =
            Page(if (path.isEmpty()) listOf(AssetRow(library, folder)) else listOf(AssetRow(library, file)), null)
        override suspend fun search(query: String, scope: SearchScope, cursor: String?) = Page(listOf(AssetRow(library, file)), null)
        override suspend fun detail(libraryId: String, entryId: String) = AssetRow(library, file)
        override suspend fun scan(libraryId: String): Scan? = null
    }
    @Test fun nativeBrowseSearchDetailAndLogout() {
        val model = WorkspaceModel(ServerProfile.parse("https://localhost", ""), createApi = { fake })
        compose.setContent { WorkspaceTheme { WorkspaceApp(model) } }
        compose.onNodeWithText("账号").performTextInput("sample")
        compose.onNodeWithText("口令").performTextInput("fixture")
        compose.onNodeWithText("登录", useUnmergedTree = true).performScrollTo().performClick()
        compose.waitUntil(5000) { model.state.value.libraries.isNotEmpty() }
        compose.onAllNodesWithText("设计素材").onLast().performClick()
        compose.onNodeWithText("2026", useUnmergedTree = true).performClick()
        compose.onNodeWithText("山间湖畔.jpg", useUnmergedTree = true).assertIsDisplayed()
        capture("native-browser")
        compose.onNodeWithContentDescription("山间湖畔.jpg 文件信息").performClick()
        compose.onNodeWithText("真实相对路径").assertExists()
        capture("native-detail")
        compose.onNodeWithText("在文件夹中定位").performScrollTo().performClick()
        compose.runOnIdle { assertEquals("photo", model.state.value.location.anchor) }
        compose.onNodeWithContentDescription("搜索").performClick()
        compose.onNodeWithText("搜索文件名或路径").performTextInput("山间")
        compose.waitUntil(5000) { model.state.value.rows.isNotEmpty() && !model.state.value.loading }
        compose.onNodeWithText("山间湖畔.jpg", useUnmergedTree = true).assertIsDisplayed()
        compose.runOnIdle { model.logout() }
        compose.onNodeWithText("登录", useUnmergedTree = true).assertExists()
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
