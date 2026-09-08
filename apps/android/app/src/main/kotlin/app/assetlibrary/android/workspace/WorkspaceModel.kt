package app.assetlibrary.android.workspace

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import app.assetlibrary.android.protocol.ApiFailure
import app.assetlibrary.android.protocol.AssetApi
import app.assetlibrary.android.protocol.AssetLinkClient
import app.assetlibrary.android.protocol.AssetRow
import app.assetlibrary.android.protocol.BrowseOptions
import app.assetlibrary.android.protocol.Library
import app.assetlibrary.android.protocol.Scan
import app.assetlibrary.android.protocol.SearchScope
import app.assetlibrary.android.protocol.ServerProfile
import app.assetlibrary.android.protocol.Session
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import java.time.Duration
import java.time.Instant

enum class Screen { LIBRARIES, BROWSE, SEARCH, CONNECTION }
data class Location(
    val screen: Screen = Screen.LIBRARIES, val library: Library? = null, val path: String = "",
    val query: String = "", val scope: String = "all", val options: BrowseOptions = BrowseOptions(), val anchor: String? = null,
)
data class WorkspaceState(
    val profile: ServerProfile? = null, val session: Session? = null,
    val location: Location = Location(), val libraries: List<Library> = emptyList(), val category: String? = null,
    val librariesNext: String? = null, val librariesPage: Int = 1,
    val rows: List<AssetRow> = emptyList(), val next: String? = null, val page: Int = 1,
    val hasPreviousPage: Boolean = false, val hasPreviousLibraries: Boolean = false,
    val detail: AssetRow? = null, val scan: Scan? = null,
    val preview: AssetRow? = null,
    val loading: Boolean = false, val error: String? = null, val stale: Boolean = false,
    val checkingSession: Boolean = false,
    val grid: Boolean = true, val canBack: Boolean = false, val canForward: Boolean = false,
)

class WorkspaceModel(
    profile: ServerProfile?,
    private val saveProfile: (ServerProfile) -> Unit = {},
    private val createApi: (ServerProfile) -> AssetApi = ::AssetLinkClient,
) : ViewModel() {
    private val mutable = MutableStateFlow(WorkspaceState(profile = profile))
    val state = mutable.asStateFlow()
    private var api: AssetApi? = null
    private var request: Job? = null
    private var expiry: Job? = null
    private var revision = 0L
    private val past = ArrayDeque<Location>()
    private val future = ArrayDeque<Location>()
    private val cursors = mutableListOf<String?>(null)
    private val libraryCursors = mutableListOf<String?>(null)
    val images = WorkspaceImages(viewModelScope, { key -> requireNotNull(api).image(key.library, key.entry, key.variant) }) { failure ->
        reset(state.value.profile)
        mutable.value = state.value.copy(error = failure.userMessage)
    }

    fun visibleImages(rows: List<AssetRow>) {
        val current = state.value.rows.map { it.library.id to it.entry.id }.toSet()
        images.visible(rows.filter { (it.library.id to it.entry.id) in current })
    }

    fun openPreview(row: AssetRow) {
        if (row.entry.kind != "file") { select(row); return }
        request?.cancel(); revision++
        mutable.value = state.value.copy(preview = row, detail = null, loading = false)
        images.preview(row)
    }

    fun closePreview() { mutable.value = state.value.copy(preview = null); images.preview(null) }

    fun onBackground() {
        if (state.value.session == null) return
        request?.cancel(); revision++
        images.pause()
        mutable.value = state.value.copy(preview = null, checkingSession = true, loading = false)
    }

    fun connect(address: String, fingerprint: String, account: String, password: String) {
        val profile = try { ServerProfile.parse(address, fingerprint) } catch (_: Exception) {
            mutable.value = state.value.copy(error = "请输入有效的 HTTPS 服务器根地址和可选的 64 位 SHA256 证书指纹。")
            return
        }
        if (account.isBlank() || password.isEmpty()) {
            mutable.value = state.value.copy(error = "请输入账号与口令。")
            return
        }
        reset(profile)
        val client = createApi(profile)
        api = client
        runRequest {
            val session = client.signIn(account.trim(), password)
            currentCoroutineContext().ensureActive()
            saveProfile(profile)
            mutable.value = state.value.copy(session = session)
            images.resume()
            expiry = viewModelScope.launch {
                delay(Duration.between(Instant.now(), session.expiresAt).toMillis().coerceAtLeast(1))
                reset(profile)
                mutable.value = state.value.copy(error = "登录已到期，请重新登录。")
            }
            loadLibraries()
        }
    }

    fun logout() {
        images.pause()
        val client = api
        val profile = state.value.profile
        request?.cancel()
        expiry?.cancel()
        past.clear(); future.clear(); cursors.clear(); cursors.add(null)
        mutable.value = WorkspaceState(profile = profile, loading = true)
        runRequest {
            try { client?.signOut() } catch (_: ApiFailure) {
                mutable.value = state.value.copy(error = "已清除此设备会话；网络错误，未确认服务器退出。")
            } finally { client?.clearSession(); if (api === client) api = null }
        }
    }

    private fun reset(profile: ServerProfile?) {
        images.pause()
        revision++
        request?.cancel(); expiry?.cancel(); api?.clearSession(); api = null
        past.clear(); future.clear(); cursors.clear(); cursors.add(null)
        libraryCursors.clear(); libraryCursors.add(null)
        mutable.value = WorkspaceState(profile = profile)
    }

    fun navigate(location: Location) {
        if (location != state.value.location) {
            past.addLast(state.value.location)
            if (past.size > 64) past.removeFirst()
            future.clear()
        }
        moveTo(location)
    }

    private fun moveTo(location: Location) {
        images.clear()
        request?.cancel(); revision++
        cursors.clear(); cursors.add(null)
        mutable.value = state.value.copy(location = location, rows = emptyList(), next = null, page = 1, hasPreviousPage = false,
            detail = null, preview = null, scan = null, error = null, stale = false, loading = false,
            canBack = past.isNotEmpty(), canForward = future.isNotEmpty())
        if (location.screen != Screen.CONNECTION) refresh()
    }

    fun back(): Boolean {
        if (state.value.preview != null) { closePreview(); return true }
        if (state.value.detail != null) { closeDetail(); return true }
        if (past.isEmpty()) return false
        future.addLast(state.value.location)
        moveTo(past.removeLast())
        return true
    }
    fun forward() {
        if (future.isEmpty()) return
        past.addLast(state.value.location)
        moveTo(future.removeLast())
    }
    fun openLibrary(library: Library) = navigate(Location(Screen.BROWSE, library))
    fun openRow(row: AssetRow) {
        if (row.entry.directory) navigate(Location(Screen.BROWSE, row.library, row.entry.path))
        else openPreview(row)
    }
    fun up() {
        val current = state.value.location
        if (current.path.isEmpty()) navigate(Location())
        else navigate(current.copy(path = current.path.substringBeforeLast('/', ""), anchor = null))
    }
    fun locate(row: AssetRow) = navigate(Location(Screen.BROWSE, row.library, row.entry.path.substringBeforeLast('/', ""), anchor = row.entry.id))
    fun select(row: AssetRow) {
        mutable.value = state.value.copy(detail = null)
        runRequest {
            val detail = requireNotNull(api).detail(row.library.id, row.entry.id)
            currentCoroutineContext().ensureActive()
            mutable.value = state.value.copy(detail = detail)
        }
    }
    fun closeDetail() { cancel(); mutable.value = state.value.copy(detail = null) }
    fun toggleGrid() { mutable.value = state.value.copy(grid = !state.value.grid) }
    fun category(value: String?) {
        libraryCursors.clear(); libraryCursors.add(null)
        mutable.value = state.value.copy(category = value, libraries = emptyList(), librariesNext = null, librariesPage = 1, hasPreviousLibraries = false)
        runRequest { loadLibraries() }
    }
    fun librariesPage(next: Boolean) {
        if (next) state.value.librariesNext?.let { libraryCursors.add(it) } ?: return
        else if (libraryCursors.size > 1) libraryCursors.removeAt(libraryCursors.lastIndex) else return
        if (libraryCursors.size > 65) libraryCursors.removeAt(0)
        mutable.value = state.value.copy(librariesPage = state.value.librariesPage + if (next) 1 else -1,
            libraries = emptyList(), hasPreviousLibraries = libraryCursors.size > 1)
        runRequest { loadLibraries() }
    }
    fun options(value: BrowseOptions) = moveTo(state.value.location.copy(options = value, anchor = null))
    fun searchText(value: String) {
        val current = state.value.location
        moveTo(current.copy(query = value.take(512)))
    }
    fun searchScope(value: String) = moveTo(state.value.location.copy(scope = value))
    fun showSearch() {
        val current = state.value.location
        navigate(Location(Screen.SEARCH, current.library, current.path, scope = if (current.library == null) "all" else "library"))
    }
    fun nextPage() {
        val cursor = state.value.next ?: return
        if (cursor == cursors.last()) { mutable.value = state.value.copy(error = "分页游标未前进，请刷新目录。"); return }
        cursors.add(cursor)
        if (cursors.size > 65) cursors.removeAt(0)
        images.clear()
        mutable.value = state.value.copy(page = state.value.page + 1, rows = emptyList(), detail = null, preview = null, hasPreviousPage = true)
        loadCurrent()
    }
    fun previousPage() {
        if (cursors.size <= 1) return
        cursors.removeAt(cursors.lastIndex)
        images.clear()
        mutable.value = state.value.copy(page = (state.value.page - 1).coerceAtLeast(1), rows = emptyList(), detail = null, preview = null, hasPreviousPage = cursors.size > 1)
        loadCurrent()
    }
    fun refresh() {
        if (state.value.session == null) return
        images.refresh()
        cursors.clear(); cursors.add(null)
        libraryCursors.clear(); libraryCursors.add(null)
        mutable.value = state.value.copy(page = 1, librariesPage = 1, next = null, librariesNext = null, hasPreviousPage = false, hasPreviousLibraries = false)
        if (state.value.location.screen == Screen.LIBRARIES) runRequest { loadLibraries() }
        else loadCurrent()
    }
    fun onForeground() {
        val session = state.value.session ?: return
        images.pause()
        if (session.expiresAt <= Instant.now()) {
            reset(state.value.profile)
            mutable.value = state.value.copy(error = "登录已到期，请重新登录。")
            return
        }
        mutable.value = state.value.copy(checkingSession = true, preview = null)
        runRequest {
            val verified = requireNotNull(api).validateSession()
            currentCoroutineContext().ensureActive()
            if (verified.principalId != session.principalId) throw ApiFailure(401, "identity_changed")
            // A valid account session does not prove that its previous library grants still hold.
            val selected = state.value.detail
            mutable.value = state.value.copy(session = verified, detail = null)
            loadLibraries()
            val current = state.value.location
            if (current.screen == Screen.BROWSE || (current.screen == Screen.SEARCH && current.query.isNotBlank())) {
                val page = try { fetchCurrent(current) } catch (failure: ApiFailure) {
                    if (failure.status != 400 || cursors.last() == null) throw failure
                    cursors.clear(); cursors.add(null)
                    mutable.value = state.value.copy(page = 1, hasPreviousPage = false)
                    fetchCurrent(current)
                }
                currentCoroutineContext().ensureActive()
                mutable.value = state.value.copy(rows = page.items, next = page.next, stale = false)
            }
            if (selected != null) {
                val detail = requireNotNull(api).detail(selected.library.id, selected.entry.id)
                currentCoroutineContext().ensureActive()
                mutable.value = state.value.copy(detail = detail)
            }
            images.resume()
            mutable.value = state.value.copy(checkingSession = false)
        }
    }
    fun cancel() {
        revision++
        request?.cancel()
        mutable.value = state.value.copy(loading = false)
    }
    fun loadScan() {
        val library = state.value.location.library ?: return
        runRequest {
            val scan = requireNotNull(api).scan(library.id)
            currentCoroutineContext().ensureActive()
            mutable.value = state.value.copy(scan = scan)
        }
    }
    private suspend fun loadLibraries() {
        val page = requireNotNull(api).libraries(libraryCursors.last(), state.value.category)
        currentCoroutineContext().ensureActive()
        mutable.value = state.value.copy(libraries = page.items, librariesNext = page.next)
    }
    private fun loadCurrent() {
        val current = state.value.location
        if (current.screen == Screen.SEARCH && current.query.isBlank()) return
        runRequest {
            if (current.screen != Screen.BROWSE && current.screen != Screen.SEARCH) return@runRequest
            if (current.screen == Screen.SEARCH) delay(300)
            val page = fetchCurrent(current)
            currentCoroutineContext().ensureActive()
            mutable.value = state.value.copy(rows = page.items, next = page.next, stale = false)
        }
    }
    private suspend fun fetchCurrent(current: Location): app.assetlibrary.android.protocol.Page<AssetRow> {
        val client = requireNotNull(api)
        return if (current.screen == Screen.BROWSE)
            client.browse(requireNotNull(current.library), current.path, cursors.last(), current.options, current.anchor)
        else client.search(current.query.trim(), SearchScope(current.scope, if (current.scope == "all") null else current.library?.id, current.path), cursors.last())
    }
    private fun runRequest(action: suspend () -> Unit) {
        request?.cancel()
        val currentRevision = ++revision
        mutable.value = state.value.copy(loading = true, error = null)
        request = viewModelScope.launch {
            try { action() } catch (error: Exception) {
                if (error is CancellationException) throw error
                if (currentRevision != revision) return@launch
                val failure = error as? ApiFailure ?: ApiFailure(0, "invalid_response")
                if (failure.status == 401 || failure.status == 403) {
                    reset(state.value.profile)
                    mutable.value = state.value.copy(error = if (failure.status == 401) "登录失败或已失效，请检查账号与口令后重新登录。" else failure.userMessage)
                } else if (failure.status == 404) {
                    images.clear()
                    mutable.value = state.value.copy(rows = emptyList(), detail = null, preview = null, next = null, error = failure.userMessage, stale = false)
                } else {
                    mutable.value = state.value.copy(error = failure.userMessage, stale = state.value.rows.isNotEmpty())
                }
            } finally {
                if (currentRevision == revision) mutable.value = state.value.copy(loading = false)
            }
        }
    }
    override fun onCleared() { images.pause(); api?.clearSession() }
}
