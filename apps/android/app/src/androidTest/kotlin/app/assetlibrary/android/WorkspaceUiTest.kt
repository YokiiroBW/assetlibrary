package app.assetlibrary.android

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.v2.createComposeRule
import androidx.test.platform.app.InstrumentationRegistry
import app.assetlibrary.android.protocol.*
import app.assetlibrary.android.ui.WorkspaceApp
import app.assetlibrary.android.ui.WorkspaceTheme
import app.assetlibrary.android.workspace.WorkspaceModel
import app.assetlibrary.android.workspace.WorkspaceImages
import app.assetlibrary.android.workspace.ImageKey
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.cancel
import kotlinx.coroutines.awaitCancellation
import java.io.ByteArrayOutputStream
import java.util.concurrent.atomic.AtomicInteger
import org.junit.Assert.*
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
    private var imageFailure: ApiFailure? = null
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
        override suspend fun image(libraryId: String, entryId: String, variant: ImageVariant): ImagePayload {
            imageFailure?.let { throw it }
            return sampleImage(variant)
        }
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

    @Test fun previewZoomCloseAndBackgroundPrivacy() {
        val model = WorkspaceModel(null, createApi = { fake })
        compose.setContent { WorkspaceTheme { WorkspaceApp(model) } }
        compose.runOnIdle { model.connect("https://localhost", "", "sample", "fixture") }
        compose.waitUntil(5000) { model.state.value.session != null }
        compose.runOnIdle { model.openLibrary(library) }
        compose.onNodeWithText("2026", useUnmergedTree = true).performClick()
        compose.waitUntil(5000) { model.images.state.value.values.any { it.bitmap != null } }
        capture("native-image-thumbnails")
        compose.onNodeWithText("山间湖畔.jpg", useUnmergedTree = true).performClick()
        compose.waitUntil(5000) { model.images.state.value[ImageKey.of(AssetRow(library, file), ImageVariant.PREVIEW)]?.bitmap != null }
        compose.onNodeWithContentDescription("山间湖畔.jpg 图片预览").assertIsDisplayed()
        compose.onNodeWithText("放大").performClick()
        compose.onNodeWithText("150%").assertIsDisplayed()
        compose.onNodeWithText("复位").performClick()
        compose.onNodeWithText("100%").assertIsDisplayed()
        compose.onNodeWithContentDescription("山间湖畔.jpg 图片预览").performTouchInput { doubleClick() }
        compose.onNodeWithText("200%").assertIsDisplayed()
        compose.onNodeWithText("复位").performClick()
        capture("native-image-preview")
        compose.onNodeWithText("关闭预览").performClick()
        compose.runOnIdle { assertNull(model.state.value.preview) }
        compose.onNodeWithText("山间湖畔.jpg", useUnmergedTree = true).performClick()
        compose.runOnIdle { model.onBackground() }
        compose.onNodeWithText("正在检查登录状态…").assertIsDisplayed()
        compose.runOnIdle { assertTrue(model.images.state.value.isEmpty()); model.logout() }
    }

    @Test fun sampledBitmapCacheRequiresNewAuthorizedBytesAndIsClearedOnPause() {
        val scope = CoroutineScope(Dispatchers.Main + Job())
        val calls = AtomicInteger()
        val images = WorkspaceImages(scope, { calls.incrementAndGet(); sampleImage(it.variant) }, { fail() })
        val row = AssetRow(library, file)
        val key = ImageKey.of(row, ImageVariant.THUMBNAIL)
        try {
            compose.runOnIdle { images.resume(); images.visible(listOf(row)) }
            compose.waitUntil(5000) { images.state.value[key]?.bitmap != null }
            val first = requireNotNull(images.state.value[key]?.bitmap)
            assertEquals(256, first.width)
            assertEquals(128, first.height)
            assertEquals(0, Color.alpha(first.getPixel(0, 0)))
            compose.runOnIdle { images.visible(emptyList()); images.visible(listOf(row)) }
            compose.waitUntil(5000) { images.state.value[key]?.bitmap != null }
            assertEquals(2, calls.get())
            assertSame(first, images.state.value[key]?.bitmap)
            compose.runOnIdle { images.pause(); images.resume(); images.visible(listOf(row)) }
            compose.waitUntil(5000) { images.state.value[key]?.bitmap != null }
            assertEquals(3, calls.get())
            assertNotSame(first, images.state.value[key]?.bitmap)
        } finally { compose.runOnIdle { images.pause(); scope.cancel() } }
    }

    @Test fun immediateMainCancellationClearsTwoInFlightRequestsSafely() {
        val scope = CoroutineScope(Dispatchers.Main.immediate + Job())
        var active = 0
        val images = WorkspaceImages(scope, { active++; try { awaitCancellation() } finally { active-- } }, { fail() })
        try {
            compose.runOnIdle {
                images.resume()
                images.visible(listOf(AssetRow(library, file), AssetRow(library, file.copy(id = "other"))))
                assertEquals(2, active)
                images.pause()
                assertEquals(0, active)
                assertTrue(images.state.value.isEmpty())
            }
        } finally { scope.cancel() }
    }

    @Test fun previewFailuresRetainFileInformationAndDeniedSessionClearsWorkspace() {
        val model = WorkspaceModel(null, createApi = { fake })
        compose.setContent { WorkspaceTheme { WorkspaceApp(model) } }
        compose.runOnIdle { model.connect("https://localhost", "", "sample", "fixture") }
        compose.waitUntil(5000) { model.state.value.session != null }
        val row = AssetRow(library, file)
        for (failure in listOf(ApiFailure(415, "preview_unsupported"), ApiFailure(422, "preview_invalid"),
            ApiFailure(422, "preview_limit_exceeded"), ApiFailure(503, "preview_unavailable"), ApiFailure(409, "source_changed"),
            ApiFailure(0, "network"), ApiFailure(504, "preview_timeout"), ApiFailure(404, "not_found"))) {
            compose.runOnIdle { model.closePreview(); imageFailure = failure; model.openPreview(row) }
            compose.waitUntil(5000) { model.images.state.value.values.any { it.error != null } }
            compose.onNodeWithText(failure.imageMessage()).assertIsDisplayed()
            compose.onNodeWithText("查看文件信息").assertExists()
            if (failure.status == 404) {
                compose.onNodeWithText("图片预览不可用，可查看文件信息").assertIsDisplayed()
                compose.onNodeWithText("查看文件信息").performClick()
                compose.waitUntil(5000) { model.state.value.detail != null }
                compose.onNodeWithText("真实相对路径").assertExists()
                compose.onNodeWithText(file.path).performScrollTo().assertIsDisplayed()
                compose.runOnIdle {
                    assertEquals(row, model.state.value.detail)
                    assertNotNull(model.state.value.session)
                }
                compose.onNodeWithText("关闭").performClick()
            }
        }
        compose.runOnIdle { model.closePreview(); imageFailure = ApiFailure(403, "denied"); model.openPreview(row) }
        compose.waitUntil(5000) { model.state.value.session == null }
        compose.runOnIdle { assertTrue(model.images.state.value.isEmpty()); assertNull(model.state.value.preview) }
        compose.onNodeWithText("登录", useUnmergedTree = true).assertExists()
    }

    private fun sampleImage(variant: ImageVariant): ImagePayload {
        val bitmap = Bitmap.createBitmap(variant.maxEdge, variant.maxEdge / 2, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(bitmap)
        val paint = Paint().apply { color = Color.rgb(46, 133, 124) }
        canvas.drawRect(bitmap.width / 8f, bitmap.height / 4f, bitmap.width * 0.85f, bitmap.height * 0.85f, paint)
        paint.color = Color.rgb(235, 171, 62)
        canvas.drawCircle(bitmap.width * 0.7f, bitmap.height * 0.35f, bitmap.height / 5f, paint)
        val bytes = ByteArrayOutputStream().also { bitmap.compress(Bitmap.CompressFormat.PNG, 100, it) }.toByteArray()
        bitmap.recycle()
        return ImagePayload.parse(bytes, variant)
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
