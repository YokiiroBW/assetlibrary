@file:OptIn(kotlinx.coroutines.ExperimentalCoroutinesApi::class)
package app.assetlibrary.android.workspace

import app.assetlibrary.android.protocol.*
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import kotlinx.coroutines.test.resetMain
import org.junit.After
import org.junit.Assert.*
import org.junit.Before
import org.junit.Test
import java.time.Instant

class WorkspaceModelTest {
    private val dispatcher = StandardTestDispatcher()
    private val library = Library("sample", "样例库", "online", "read_only", "images")
    private val entry = Entry("one", "sample", "folder/photo.jpg", "photo.jpg", "file", "1234", "2026-09-08T00:00:00Z")
    private lateinit var api: FakeApi
    private lateinit var model: WorkspaceModel
    @Before fun setup() {
        Dispatchers.setMain(dispatcher)
        api = FakeApi()
        model = WorkspaceModel(null, createApi = { api })
    }
    @After fun cleanup() { model.cancel(); Dispatchers.resetMain() }
    private inner class FakeApi : AssetApi {
        var error: ApiFailure? = null
        var cleared = false
        var query = ""
        var options: BrowseOptions? = null
        override suspend fun signIn(account: String, password: String) = Session("样例", false, Instant.now().plusSeconds(3600))
        override suspend fun validateSession() = Session("样例", false, Instant.now().plusSeconds(3600))
        override suspend fun signOut() { error?.let { throw it } }
        override fun clearSession() { cleared = true }
        override suspend fun libraries(cursor: String?, category: String?) = Page(listOf(library), null)
        override suspend fun browse(library: Library, path: String, cursor: String?, options: BrowseOptions, anchor: String?): Page<AssetRow> {
            this.options = options
            error?.let { throw it }
            return Page(listOf(AssetRow(library, entry)), if (cursor == null) "next" else null)
        }
        override suspend fun search(query: String, scope: SearchScope, cursor: String?): Page<AssetRow> {
            this.query = query
            delay(if (query == "old") 1000 else 10)
            return Page(listOf(AssetRow(library, entry.copy(name = query))), null)
        }
        override suspend fun detail(libraryId: String, entryId: String): AssetRow { error?.let { throw it }; return AssetRow(library, entry) }
        override suspend fun scan(libraryId: String): Scan? = null
        override suspend fun image(libraryId: String, entryId: String, variant: ImageVariant): ImagePayload = throw ApiFailure(415, "preview_unsupported")
    }
    private fun login() { model.connect("https://localhost", "", "sample", "fixture") }
    @Test fun `paging replaces bounded page and back restores prior cursor`() = runTest(dispatcher) {
        login(); runCurrent(); model.openLibrary(library); runCurrent()
        assertEquals(1, model.state.value.rows.size)
        model.nextPage(); runCurrent()
        assertEquals(2, model.state.value.page)
        assertEquals(1, model.state.value.rows.size)
        model.previousPage(); runCurrent(); assertEquals(1, model.state.value.page)
    }
    @Test fun `sort and filter are sent to authoritative query`() = runTest(dispatcher) {
        login(); runCurrent(); model.openLibrary(library); runCurrent()
        val chosen = BrowseOptions("size", "desc", "files", "photo")
        model.options(chosen); runCurrent(); assertEquals(chosen, api.options)
    }
    @Test fun `new search cancels old response`() = runTest(dispatcher) {
        login(); runCurrent(); model.showSearch()
        model.searchText("old"); advanceTimeBy(400); runCurrent()
        model.searchText("new"); advanceTimeBy(400); runCurrent()
        assertEquals("new", model.state.value.rows.single().entry.name)
        advanceTimeBy(2000); runCurrent()
        assertEquals("new", model.state.value.rows.single().entry.name)
    }
    @Test fun `denied refresh clears libraries results details and session`() = runTest(dispatcher) {
        login(); runCurrent(); model.openLibrary(library); runCurrent()
        model.select(AssetRow(library, entry)); runCurrent()
        api.error = ApiFailure(403, "denied"); model.refresh(); runCurrent()
        assertNull(model.state.value.session)
        assertTrue(model.state.value.rows.isEmpty() && model.state.value.libraries.isEmpty())
        assertNull(model.state.value.detail)
        assertTrue(api.cleared)
    }
    @Test fun `network failure marks old snapshot while 404 removes it`() = runTest(dispatcher) {
        login(); runCurrent(); model.openLibrary(library); runCurrent()
        api.error = ApiFailure(0, "network"); model.refresh(); runCurrent()
        assertTrue(model.state.value.stale)
        api.error = ApiFailure(404, "missing"); model.refresh(); runCurrent()
        assertTrue(model.state.value.rows.isEmpty())
        assertFalse(model.state.value.stale)
    }
    @Test fun `source change destroys history and old contents`() = runTest(dispatcher) {
        login(); runCurrent(); model.openLibrary(library); runCurrent()
        model.connect("https://new.localhost", "", "sample", "fixture"); runCurrent()
        assertEquals("https://new.localhost", model.state.value.profile?.origin)
        assertTrue(model.state.value.rows.isEmpty())
        assertFalse(model.state.value.canBack)
        assertTrue(api.cleared)
    }
    @Test fun `refresh after stale pagination restarts first cursor`() = runTest(dispatcher) {
        login(); runCurrent(); model.openLibrary(library); runCurrent()
        model.nextPage(); runCurrent(); assertEquals(2, model.state.value.page)
        model.refresh(); runCurrent(); assertEquals(1, model.state.value.page)
        assertEquals("next", model.state.value.next)
    }
    @Test fun `valid foreground session still hides old contents and clears revoked library grant`() = runTest(dispatcher) {
        login(); runCurrent(); model.openLibrary(library); runCurrent()
        api.error = ApiFailure(403, "revoked")
        model.onForeground()
        assertTrue(model.state.value.checkingSession)
        runCurrent()
        assertNull(model.state.value.session)
        assertTrue(model.state.value.rows.isEmpty())
    }
    @Test fun `background closes preview clears images and masks workspace until permission recheck`() = runTest(dispatcher) {
        login(); runCurrent(); model.openLibrary(library); runCurrent()
        model.openPreview(AssetRow(library, entry)); runCurrent()
        assertNotNull(model.state.value.preview)
        model.onBackground(); runCurrent()
        assertNull(model.state.value.preview)
        assertTrue(model.images.state.value.isEmpty())
        assertTrue(model.state.value.checkingSession)
        model.onForeground(); runCurrent()
        assertFalse(model.state.value.checkingSession)
        assertEquals(1, model.state.value.rows.size)
    }
    @Test fun `preview back preserves directory location and source switch removes picture state`() = runTest(dispatcher) {
        login(); runCurrent(); model.openLibrary(library); runCurrent()
        val location = model.state.value.location
        model.openPreview(AssetRow(library, entry)); runCurrent()
        assertTrue(model.back())
        assertNull(model.state.value.preview)
        assertEquals(location, model.state.value.location)
        model.openPreview(AssetRow(library, entry)); runCurrent()
        model.connect("https://new.localhost", "", "sample", "fixture"); runCurrent()
        assertNull(model.state.value.preview)
        assertTrue(model.images.state.value.isEmpty())
    }
}
