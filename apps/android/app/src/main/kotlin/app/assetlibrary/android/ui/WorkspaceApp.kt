@file:OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)

package app.assetlibrary.android.ui

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import androidx.lifecycle.Lifecycle
import app.assetlibrary.android.protocol.*
import app.assetlibrary.android.workspace.*
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter

@Composable
fun WorkspaceApp(model: WorkspaceModel) {
    val state by model.state.collectAsStateWithLifecycle()
    LifecycleEventEffect(Lifecycle.Event.ON_RESUME) { model.onForeground() }
    BackHandler(state.detail != null || state.canBack) { model.back() }
    Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
        if (state.session == null) LoginScreen(state, model)
        else if (state.checkingSession) Column(Modifier.fillMaxSize().safeDrawingPadding().padding(28.dp), verticalArrangement = Arrangement.Center) {
            Text("正在检查登录状态…", style = MaterialTheme.typography.titleLarge)
            if (state.loading) LinearProgressIndicator(Modifier.fillMaxWidth().padding(vertical = 16.dp))
            state.error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
            TextButton(model::onForeground, enabled = !state.loading) { Text("重新连接") }
            TextButton(model::logout) { Text("退出") }
        }
        else BoxWithConstraints(Modifier.fillMaxSize()) {
            val wide = maxWidth >= 960.dp
            Scaffold(containerColor = MaterialTheme.colorScheme.background, bottomBar = {
                if (!wide) NavigationBar(containerColor = MaterialTheme.colorScheme.surface) {
                    NavigationBarItem(state.location.screen in listOf(Screen.LIBRARIES, Screen.BROWSE), { model.navigate(Location()) },
                        icon = { AppIcon("library") }, label = { Text("资源库") })
                    NavigationBarItem(state.location.screen == Screen.SEARCH, model::showSearch,
                        icon = { AppIcon("search") }, label = { Text("搜索") })
                    NavigationBarItem(state.location.screen == Screen.CONNECTION, { model.navigate(Location(Screen.CONNECTION)) },
                        icon = { AppIcon("connection") }, label = { Text("连接") })
                }
            }) { padding ->
                Row(Modifier.padding(padding).fillMaxSize()) {
                    if (wide) {
                        LibrarySidebar(state, model, Modifier.width(204.dp).fillMaxHeight())
                        VerticalDivider()
                    }
                    Column(Modifier.weight(1f).fillMaxHeight()) {
                        Header(state, model, wide)
                        if (state.error != null) MessagePanel(requireNotNull(state.error), state.stale, model::refresh)
                        if (state.loading) Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp), verticalAlignment = Alignment.CenterVertically) {
                            LinearProgressIndicator(Modifier.weight(1f))
                            TextButton(model::cancel) { Text("取消") }
                        }
                        when (state.location.screen) {
                            Screen.LIBRARIES -> LibraryHome(state, model)
                            Screen.BROWSE, Screen.SEARCH -> AssetBrowser(state, model)
                            Screen.CONNECTION -> ConnectionScreen(state, model)
                        }
                    }
                    if (wide && state.location.screen != Screen.CONNECTION) {
                        VerticalDivider()
                        Surface(Modifier.width(280.dp).fillMaxHeight()) {
                            if (state.detail == null) Column(Modifier.padding(24.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                                Text("文件信息", style = MaterialTheme.typography.titleMedium)
                                Text("选择文件查看真实相对路径、类型、大小和修改时间。", color = LocalWorkspaceColors.current.muted)
                            } else EntryDetail(requireNotNull(state.detail), model::closeDetail, model::locate)
                        }
                    }
                }
            }
            if (!wide && state.detail != null) {
                ModalBottomSheet(onDismissRequest = model::closeDetail, sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true)) {
                    EntryDetail(requireNotNull(state.detail), model::closeDetail, model::locate, Modifier.fillMaxHeight(0.86f))
                }
            }
        }
    }
}

@Composable
private fun LoginScreen(state: WorkspaceState, model: WorkspaceModel) {
    var origin by rememberSaveable { mutableStateOf(state.profile?.origin ?: "") }
    var pin by rememberSaveable { mutableStateOf(state.profile?.certificatePin ?: "") }
    var account by remember { mutableStateOf("") }
    // Credentials deliberately do not enter SavedState, preferences or Activity instance bundles.
    var password by remember { mutableStateOf("") }
    var showPin by rememberSaveable { mutableStateOf(pin.isNotEmpty()) }
    val focus = LocalFocusManager.current
    val submit = { focus.clearFocus(); model.connect(origin, pin, account, password); password = "" }
    Box(Modifier.fillMaxSize().safeDrawingPadding().imePadding(), contentAlignment = Alignment.Center) {
        Column(Modifier.widthIn(max = 520.dp).fillMaxWidth().verticalScroll(rememberScrollState()).padding(28.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                AppIcon("library", Modifier.size(40.dp))
                Column { Text("AssetLibrary", style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold); Text("你的文件，原来的位置", color = LocalWorkspaceColors.current.muted) }
            }
            Spacer(Modifier.height(8.dp))
            Text("连接资源库", style = MaterialTheme.typography.headlineMedium)
            Text("使用与 Web 相同的服务器地址和账号，浏览 NAS 的真实目录。", color = LocalWorkspaceColors.current.muted)
            OutlinedTextField(origin, { origin = it }, Modifier.fillMaxWidth(), label = { Text("HTTPS 服务器地址") },
                placeholder = { Text("https://nas.example:5443") }, singleLine = true, enabled = !state.loading,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri, imeAction = ImeAction.Next))
            TextButton({ showPin = !showPin }, contentPadding = PaddingValues(0.dp)) { Text(if (showPin) "收起证书设置" else "NAS 自签证书设置") }
            if (showPin) {
                OutlinedTextField(pin, { pin = it }, Modifier.fillMaxWidth(), label = { Text("叶证书 SHA256 指纹（可选）") },
                    minLines = 2, maxLines = 3, enabled = !state.loading, supportingText = { Text("仅填写你已从服务器管理员核对的指纹。主机名与有效期仍会验证。") })
            }
            OutlinedTextField(account, { account = it }, Modifier.fillMaxWidth(), label = { Text("账号") }, singleLine = true, enabled = !state.loading,
                keyboardOptions = KeyboardOptions(imeAction = ImeAction.Next))
            OutlinedTextField(password, { password = it }, Modifier.fillMaxWidth(), label = { Text("口令") }, singleLine = true, enabled = !state.loading,
                visualTransformation = PasswordVisualTransformation(), keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password, imeAction = ImeAction.Done),
                keyboardActions = KeyboardActions(onDone = { if (!state.loading) submit() }))
            if (state.error != null) Text(requireNotNull(state.error), color = MaterialTheme.colorScheme.error)
            Button(submit, Modifier.fillMaxWidth().heightIn(min = 48.dp), enabled = !state.loading) { Text(if (state.loading) "正在连接…" else "登录") }
            if (state.loading) TextButton(model::cancel, Modifier.align(Alignment.CenterHorizontally)) { Text("取消连接") }
            Text("只读首版 · 原文件保持不变\n关闭应用后需重新登录。", style = MaterialTheme.typography.bodySmall, color = LocalWorkspaceColors.current.muted)
        }
    }
}

@Composable
private fun Header(state: WorkspaceState, model: WorkspaceModel, wide: Boolean) {
    var switcher by remember { mutableStateOf(false) }
    Column(Modifier.background(MaterialTheme.colorScheme.surface)) {
        Row(Modifier.fillMaxWidth().padding(start = 8.dp, end = 4.dp, top = 4.dp, bottom = 4.dp), verticalAlignment = Alignment.CenterVertically) {
            if (state.canBack) IconButton({ model.back() }) { AppIcon("back", description = "返回") }
            Column(Modifier.weight(1f)) {
                val title = when (state.location.screen) { Screen.LIBRARIES -> "我的资源库"; Screen.SEARCH -> "搜索资产"; Screen.CONNECTION -> "服务器连接"; Screen.BROWSE -> state.location.library?.name ?: "资源库" }
                TextButton({ switcher = !switcher }, contentPadding = PaddingValues(horizontal = 8.dp)) {
                    Text(title, style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    if (!wide) Text(" ▾")
                }
                DropdownMenu(expanded = switcher, onDismissRequest = { switcher = false }, modifier = Modifier.heightIn(max = 400.dp)) {
                    DropdownMenuItem({ Text("全部资源库") }, { switcher = false; model.navigate(Location()) })
                    state.libraries.forEach { library -> DropdownMenuItem({ Text(library.name) }, { switcher = false; model.openLibrary(library) }) }
                }
            }
            if (state.canForward) IconButton(model::forward) { AppIcon("forward", description = "前进") }
            if (state.location.screen != Screen.SEARCH) IconButton(model::showSearch) { AppIcon("search", description = "搜索") }
            IconButton(model::refresh, enabled = !state.loading && state.location.screen != Screen.CONNECTION) { AppIcon("refresh", description = "刷新") }
        }
        HorizontalDivider()
    }
}

@Composable
private fun LibrarySidebar(state: WorkspaceState, model: WorkspaceModel, modifier: Modifier) {
    Surface(modifier) {
        Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(Modifier.padding(vertical = 12.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                AppIcon("library"); Text("AssetLibrary", fontWeight = FontWeight.Bold)
            }
            NavigationDrawerItem({ Text("资源库") }, state.location.screen == Screen.LIBRARIES, { model.navigate(Location()) }, icon = { AppIcon("library") })
            NavigationDrawerItem({ Text("搜索") }, state.location.screen == Screen.SEARCH, model::showSearch, icon = { AppIcon("search") })
            HorizontalDivider()
            Text("库", Modifier.padding(8.dp), style = MaterialTheme.typography.labelLarge, color = LocalWorkspaceColors.current.muted)
            LazyColumn(Modifier.weight(1f)) {
                items(state.libraries, key = { it.id }) { library ->
                    NavigationDrawerItem({ Text(library.name, maxLines = 2, overflow = TextOverflow.Ellipsis) }, state.location.library?.id == library.id,
                        { model.openLibrary(library) }, icon = { AppIcon("folder") })
                }
            }
            TextButton({ model.navigate(Location(Screen.CONNECTION)) }, Modifier.fillMaxWidth()) { AppIcon("connection"); Spacer(Modifier.width(8.dp)); Text("连接与账号") }
        }
    }
}

@Composable
private fun LibraryHome(state: WorkspaceState, model: WorkspaceModel) {
    Column(Modifier.fillMaxSize()) {
        Column(Modifier.padding(20.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text("从真实目录开始", style = MaterialTheme.typography.headlineSmall, modifier = Modifier.semantics { heading() })
            Text("选择一个资源库，浏览文件夹与资产。", color = LocalWorkspaceColors.current.muted)
        }
        LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            item { FilterChip(state.category == null, { model.category(null) }, { Text("全部") }) }
            items(categories.entries.toList()) { category -> FilterChip(state.category == category.key, { model.category(category.key) }, { Text(category.value) }) }
        }
        if (state.libraries.isEmpty() && !state.loading) EmptyState("暂无可见资源库", "请使用 Web 登记资源库或检查当前账号的库权限。", Modifier.weight(1f))
        else LazyColumn(Modifier.weight(1f), contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            items(state.libraries, key = { it.id }) { library ->
                Surface(Modifier.fillMaxWidth().clickable(role = Role.Button) { model.openLibrary(library) }, shape = RoundedCornerShape(12.dp), border = androidx.compose.foundation.BorderStroke(1.dp, LocalWorkspaceColors.current.line)) {
                    Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                        AppIcon("folder", Modifier.size(36.dp))
                        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(5.dp)) {
                            Text(library.name, style = MaterialTheme.typography.titleMedium)
                            Text("${categories[library.category]} · 真实物理目录", style = MaterialTheme.typography.bodySmall, color = LocalWorkspaceColors.current.muted)
                        }
                        Text(if (library.availability == "online") "在线" else "离线", style = MaterialTheme.typography.labelMedium)
                        AppIcon("forward")
                    }
                }
            }
        }
        Pager(state.librariesPage, state.hasPreviousLibraries, state.librariesNext != null, state.loading, { model.librariesPage(false) }, { model.librariesPage(true) })
    }
}

@Composable
private fun AssetBrowser(state: WorkspaceState, model: WorkspaceModel) {
    val location = state.location
    Column(Modifier.fillMaxSize()) {
        if (location.screen == Screen.SEARCH) {
            OutlinedTextField(location.query, model::searchText, Modifier.fillMaxWidth().padding(12.dp), label = { Text("搜索文件名或路径") }, singleLine = true,
                leadingIcon = { AppIcon("search") }, keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search), keyboardActions = KeyboardActions(onSearch = { model.refresh() }))
            LazyRow(contentPadding = PaddingValues(horizontal = 12.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                item { FilterChip(location.scope == "all", { model.searchScope("all") }, { Text("全部可见库") }) }
                if (location.library != null) {
                    item { FilterChip(location.scope == "library", { model.searchScope("library") }, { Text("当前资源库") }) }
                    item { FilterChip(location.scope == "directory", { model.searchScope("directory") }, { Text("当前目录及子目录") }) }
                }
            }
            Text(if (location.scope == "all") "逻辑聚合视图 · 结果显示各自物理归属" else "${location.library?.name} / ${location.path.ifEmpty { "根目录" }}", Modifier.padding(horizontal = 16.dp, vertical = 8.dp), style = MaterialTheme.typography.bodySmall, color = LocalWorkspaceColors.current.muted)
        } else {
            Row(Modifier.fillMaxWidth().padding(horizontal = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                IconButton(model::up) { AppIcon("up", description = "上一级目录") }
                Text("${location.library?.name} / ${location.path.ifEmpty { "根目录" }}", Modifier.weight(1f), style = MaterialTheme.typography.bodyMedium, maxLines = 2, overflow = TextOverflow.Ellipsis)
                IconButton(model::loadScan) { AppIcon("info", description = "查看扫描状态") }
            }
            BrowseFilters(location.options, model::options)
            if (state.scan != null) Text("扫描${when (state.scan.state) { "queued" -> "排队中"; "leased" -> "进行中"; "succeeded" -> "完成"; "failed" -> "失败"; else -> "已取消" }} · 已观察 ${state.scan.observed} · 已入索引 ${state.scan.committed}", Modifier.padding(horizontal = 16.dp), style = MaterialTheme.typography.bodySmall)
        }
        Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp), verticalAlignment = Alignment.CenterVertically) {
            Text("本页 ${state.rows.size} 项", Modifier.weight(1f), style = MaterialTheme.typography.labelLarge, color = LocalWorkspaceColors.current.muted)
            TextButton(model::toggleGrid) { AppIcon(if (state.grid) "list" else "grid"); Spacer(Modifier.width(6.dp)); Text(if (state.grid) "列表" else "网格") }
        }
        if (state.rows.isEmpty() && !state.loading) EmptyState(if (location.screen == Screen.SEARCH && location.query.isBlank()) "查找你的资产" else "没有匹配条目",
            if (location.screen == Screen.SEARCH) "输入文件名或路径，搜索只返回你有权限的结果。" else "可以调整筛选或在 Web 查看首次扫描状态。", Modifier.weight(1f))
        else if (state.grid) LazyVerticalGrid(columns = GridCells.Adaptive(144.dp), modifier = Modifier.weight(1f), contentPadding = PaddingValues(12.dp),
            horizontalArrangement = Arrangement.spacedBy(10.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            items(state.rows, key = { it.entry.libraryId + it.entry.id }) { row -> AssetTile(row, row.entry.id == (state.detail?.entry?.id ?: location.anchor), model::openRow, model::select) }
        } else LazyColumn(Modifier.weight(1f), contentPadding = PaddingValues(horizontal = 12.dp)) {
            items(state.rows, key = { it.entry.libraryId + it.entry.id }) { row ->
                Row(Modifier.fillMaxWidth().clickable(role = Role.Button) { model.openRow(row) }.padding(vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                    AppIcon(if (row.entry.directory) "folder" else "file", Modifier.padding(8.dp).size(28.dp))
                    Column(Modifier.weight(1f)) { Text(row.entry.name, maxLines = 2, overflow = TextOverflow.Ellipsis); Text("${row.library.name} · ${fileSize(row.entry.bytes)}", color = LocalWorkspaceColors.current.muted, style = MaterialTheme.typography.bodySmall) }
                    IconButton({ model.select(row) }) { AppIcon("info", description = "${row.entry.name} 文件信息") }
                }
                HorizontalDivider()
            }
        }
        Pager(state.page, state.hasPreviousPage, state.next != null, state.loading, model::previousPage, model::nextPage)
    }
}

@Composable
private fun BrowseFilters(options: BrowseOptions, change: (BrowseOptions) -> Unit) {
    var menu by remember { mutableStateOf(false) }
    var filter by rememberSaveable(options.filter) { mutableStateOf(options.filter) }
    Column(Modifier.padding(horizontal = 12.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Box {
                TextButton({ menu = true }) { Text("${when (options.sort) { "modified" -> "修改时间"; "size" -> "大小"; else -> "名称" }} ${if (options.direction == "asc") "↑" else "↓"}") }
                DropdownMenu(menu, { menu = false }) {
                    listOf("name" to "名称", "modified" to "修改时间", "size" to "大小").forEach { sort ->
                        DropdownMenuItem({ Text(sort.second) }, { menu = false; change(options.copy(sort = sort.first)) })
                    }
                    DropdownMenuItem({ Text("切换升序 / 降序") }, { menu = false; change(options.copy(direction = if (options.direction == "asc") "desc" else "asc")) })
                }
            }
            LazyRow(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                items(listOf("all" to "全部", "files" to "文件", "directories" to "文件夹")) { kind -> FilterChip(options.kind == kind.first, { change(options.copy(kind = kind.first)) }, { Text(kind.second) }) }
            }
        }
        OutlinedTextField(filter, { filter = it.take(256) }, Modifier.fillMaxWidth(), label = { Text("筛选当前目录名称") }, singleLine = true,
            trailingIcon = { TextButton({ change(options.copy(filter = filter)) }) { Text("应用") } }, keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search), keyboardActions = KeyboardActions(onSearch = { change(options.copy(filter = filter)) }))
    }
}

@Composable
private fun AssetTile(row: AssetRow, selected: Boolean, open: (AssetRow) -> Unit, info: (AssetRow) -> Unit) {
    val entry = row.entry
    Surface(Modifier.fillMaxWidth().border(if (selected) 2.dp else 1.dp, if (selected) MaterialTheme.colorScheme.primary else LocalWorkspaceColors.current.line, RoundedCornerShape(10.dp)), shape = RoundedCornerShape(10.dp)) {
        Column {
            Column(Modifier.fillMaxWidth().clickable(role = Role.Button) { open(row) }) {
                Box(Modifier.fillMaxWidth().height(104.dp).background(MaterialTheme.colorScheme.surfaceVariant), contentAlignment = Alignment.Center) {
                    AppIcon(if (entry.directory) "folder" else "file", Modifier.size(44.dp))
                    if (!entry.directory) Text(entry.name.substringAfterLast('.', "FILE").take(10).uppercase(), Modifier.align(Alignment.BottomEnd).padding(8.dp), style = MaterialTheme.typography.labelSmall, color = LocalWorkspaceColors.current.muted)
                }
                Text(entry.name, Modifier.padding(start = 10.dp, end = 10.dp, top = 10.dp), maxLines = 2, minLines = 2, overflow = TextOverflow.Ellipsis, style = MaterialTheme.typography.bodyMedium)
            }
            Row(Modifier.fillMaxWidth().padding(start = 10.dp), verticalAlignment = Alignment.CenterVertically) {
                Text(if (entry.directory) "物理文件夹" else fileSize(entry.bytes), Modifier.weight(1f), color = LocalWorkspaceColors.current.muted, style = MaterialTheme.typography.labelSmall)
                IconButton({ info(row) }) { AppIcon("info", description = "${entry.name} 文件信息") }
            }
        }
    }
}

@Composable
private fun EntryDetail(row: AssetRow, close: () -> Unit, locate: (AssetRow) -> Unit, modifier: Modifier = Modifier) {
    val context = LocalContext.current
    var copied by remember(row.entry.id) { mutableStateOf(false) }
    Column(modifier.verticalScroll(rememberScrollState()).padding(20.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("文件信息", Modifier.weight(1f), style = MaterialTheme.typography.titleMedium)
            TextButton(close) { Text("关闭") }
        }
        Box(Modifier.fillMaxWidth().height(144.dp).background(MaterialTheme.colorScheme.surfaceVariant, RoundedCornerShape(12.dp)), contentAlignment = Alignment.Center) { AppIcon(if (row.entry.directory) "folder" else "file", Modifier.size(56.dp)) }
        Text(row.entry.name, style = MaterialTheme.typography.titleLarge)
        Text("基础文件信息 · 内容预览尚未开放", style = MaterialTheme.typography.bodySmall, color = LocalWorkspaceColors.current.muted)
        HorizontalDivider()
        DetailField("资源库", row.library.name)
        DetailField("类型", when (row.entry.kind) { "directory" -> "真实文件夹"; "reparse_directory" -> "链接文件夹（不递归浏览）"; "reparse_file" -> "链接文件"; else -> "文件" })
        DetailField("大小", fileSize(row.entry.bytes))
        DetailField("修改时间", formatTime(row.entry.modified))
        DetailField("真实相对路径", row.entry.path)
        DetailField("当前访问", when (row.library.access) { "read_only" -> "只读"; "read_write" -> "可读写 / 上传"; "organize" -> "可整理"; "library_administrator" -> "资源库管理员"; else -> "不可用" })
        Button({ locate(row) }, Modifier.fillMaxWidth()) { Text("在文件夹中定位") }
        OutlinedButton({
            (context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager).setPrimaryClip(ClipData.newPlainText("资产相对路径", row.entry.path))
            copied = true
        }, Modifier.fillMaxWidth()) { Text(if (copied) "已复制相对路径" else "复制相对路径") }
    }
}

@Composable
private fun DetailField(label: String, value: String) {
    Column(verticalArrangement = Arrangement.spacedBy(5.dp)) {
        Text(label, style = MaterialTheme.typography.labelMedium, color = LocalWorkspaceColors.current.muted)
        androidx.compose.foundation.text.selection.SelectionContainer { Text(value, style = MaterialTheme.typography.bodyMedium) }
    }
}

@Composable
private fun ConnectionScreen(state: WorkspaceState, model: WorkspaceModel) {
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(24.dp), verticalArrangement = Arrangement.spacedBy(18.dp)) {
        Text("连接与账号", style = MaterialTheme.typography.headlineSmall)
        DetailField("服务器", state.profile?.origin ?: "")
        DetailField("登录账号", state.session?.displayName ?: "")
        DetailField("证书验证", if (state.profile?.certificatePin.isNullOrEmpty()) "系统信任链、主机名与有效期" else "已固定叶证书 SHA256，主机名与有效期仍验证")
        DetailField("会话到期", state.session?.expiresAt?.toString()?.let(::formatTime) ?: "")
        HorizontalDivider()
        Text("Android 只读首版", style = MaterialTheme.typography.titleMedium)
        Text("此设备不保存口令或登录会话，关闭应用后需重新登录。")
        Text("库管理和首次扫描请使用 Web。内容预览、原文件传输与同步将在对应服务端能力交付后开放。", color = LocalWorkspaceColors.current.muted)
        Button(model::logout, Modifier.heightIn(min = 48.dp)) { Text("退出并清除本机会话") }
    }
}

@Composable
private fun MessagePanel(message: String, stale: Boolean, retry: () -> Unit) {
    Surface(color = MaterialTheme.colorScheme.surfaceVariant) {
        Column(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp)) {
            Text(message, color = MaterialTheme.colorScheme.error)
            if (stale) Text("显示上次读取的旧快照。", style = MaterialTheme.typography.bodySmall)
            TextButton(retry) { Text("重试") }
        }
    }
}

@Composable
private fun Pager(page: Int, hasPrevious: Boolean, hasNext: Boolean, loading: Boolean, previous: () -> Unit, next: () -> Unit) {
    Row(Modifier.fillMaxWidth().padding(horizontal = 12.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.SpaceBetween) {
        TextButton(previous, enabled = hasPrevious && !loading) { Text("上一页") }
        Text("第 $page 页 · 每页最多 100 项", Modifier.weight(1f), textAlign = TextAlign.Center, style = MaterialTheme.typography.labelSmall, color = LocalWorkspaceColors.current.muted)
        TextButton(next, enabled = hasNext && !loading) { Text("下一页") }
    }
}

@Composable
private fun EmptyState(title: String, description: String, modifier: Modifier) {
    Column(modifier.fillMaxWidth().padding(28.dp), verticalArrangement = Arrangement.Center, horizontalAlignment = Alignment.CenterHorizontally) {
        AppIcon("folder", Modifier.size(42.dp)); Spacer(Modifier.height(16.dp))
        Text(title, style = MaterialTheme.typography.titleMedium)
        Spacer(Modifier.height(8.dp)); Text(description, color = LocalWorkspaceColors.current.muted, style = MaterialTheme.typography.bodyMedium)
    }
}

@Composable
private fun AppIcon(kind: String, modifier: Modifier = Modifier, description: String? = null) {
    val color = if (kind == "folder") LocalWorkspaceColors.current.folder else MaterialTheme.colorScheme.primary
    Canvas(modifier.size(24.dp).then(if (description == null) Modifier else Modifier.semantics { contentDescription = description })) {
        val unit = size.minDimension / 24f
        fun line(x: Float, y: Float, xx: Float, yy: Float) = drawLine(color, Offset(x * unit, y * unit), Offset(xx * unit, yy * unit), 1.8f * unit)
        when (kind) {
            "folder" -> drawPath(Path().apply { moveTo(2*unit,6*unit); lineTo(9*unit,6*unit); lineTo(12*unit,9*unit); lineTo(22*unit,9*unit); lineTo(22*unit,20*unit); lineTo(2*unit,20*unit); close() }, color, style = Stroke(1.8f*unit))
            "search" -> { drawCircle(color, 7*unit, Offset(10*unit,10*unit), style = Stroke(1.8f*unit)); line(15f,15f,22f,22f) }
            "back", "forward", "up" -> { if (kind == "back") { line(15f,5f,8f,12f); line(8f,12f,15f,19f) } else if (kind == "up") { line(5f,14f,12f,7f); line(12f,7f,19f,14f) } else { line(8f,5f,15f,12f); line(15f,12f,8f,19f) } }
            "grid" -> for (x in listOf(3,14)) for (y in listOf(3,14)) drawRect(color, Offset(x*unit,y*unit), Size(7*unit,7*unit), style = Stroke(1.6f*unit))
            "list" -> for (y in listOf(5f,12f,19f)) { line(3f,y,5f,y); line(9f,y,22f,y) }
            "info", "connection" -> { drawCircle(color,9*unit,center,style=Stroke(1.8f*unit)); line(12f,10f,12f,18f); drawCircle(color,unit,Offset(12*unit,6*unit)) }
            "refresh" -> { drawArc(color,40f,300f,false,Offset(3*unit,3*unit),Size(18*unit,18*unit),style=Stroke(1.8f*unit)); line(21f,4f,21f,10f); line(21f,10f,15f,10f) }
            else -> { drawRect(color,Offset(5*unit,3*unit),Size(14*unit,18*unit),style=Stroke(1.8f*unit)); line(8f,9f,16f,9f); line(8f,13f,16f,13f); line(8f,17f,13f,17f) }
        }
    }
}

internal fun fileSize(raw: String?): String {
    val bytes = raw?.toULongOrNull() ?: return "—"
    val number = bytes.toDouble()
    return when { number >= 1_073_741_824 -> "%.1f GB".format(number / 1_073_741_824); number >= 1_048_576 -> "%.1f MB".format(number / 1_048_576); number >= 1024 -> "%.1f KB".format(number / 1024); else -> "$raw B" }
}
private fun formatTime(value: String): String = try { DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm").withZone(ZoneId.systemDefault()).format(Instant.parse(value)) } catch (_: Exception) { value }
