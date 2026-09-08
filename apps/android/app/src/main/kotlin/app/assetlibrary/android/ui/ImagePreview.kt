package app.assetlibrary.android.ui

import androidx.compose.foundation.Image
import androidx.compose.foundation.focusable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.detectTransformGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.input.key.*
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.semantics.CustomAccessibilityAction
import androidx.compose.ui.semantics.customActions
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import app.assetlibrary.android.protocol.AssetRow
import app.assetlibrary.android.protocol.ImageVariant
import app.assetlibrary.android.workspace.ImageKey
import app.assetlibrary.android.workspace.WorkspaceModel

@Composable
internal fun ImageThumbnail(row: AssetRow, model: WorkspaceModel, modifier: Modifier = Modifier) {
    val images by model.images.state.collectAsStateWithLifecycle()
    val load = images[ImageKey.of(row, ImageVariant.THUMBNAIL)]
    Box(modifier, contentAlignment = Alignment.Center) {
        val bitmap = load?.bitmap
        if (bitmap != null) Image(bitmap.asImageBitmap(), "${row.entry.name} 缩略图", Modifier.fillMaxSize(), contentScale = ContentScale.Fit)
        else if (load?.loading == true) CircularProgressIndicator(Modifier.size(22.dp).semantics { contentDescription = "正在加载缩略图" }, strokeWidth = 2.dp)
        else Text(if (load?.error != null) "预览不可用" else "文件", style = MaterialTheme.typography.labelSmall,
            color = LocalWorkspaceColors.current.muted, modifier = Modifier.padding(4.dp).semantics { contentDescription = load?.error ?: "文件" })
    }
}

@Composable
internal fun ImagePreview(row: AssetRow, model: WorkspaceModel) {
    val images by model.images.state.collectAsStateWithLifecycle()
    val key = ImageKey.of(row, ImageVariant.PREVIEW)
    val load = images[key]
    var zoom by remember(key) { mutableFloatStateOf(1f) }
    var pan by remember(key) { mutableStateOf(Offset.Zero) }
    var viewport by remember { mutableStateOf(IntSize.Zero) }
    val bitmap = load?.bitmap
    fun bound(value: Offset, scale: Float): Offset {
        if (bitmap == null || viewport.width == 0 || viewport.height == 0) return Offset.Zero
        val fit = minOf(viewport.width.toFloat() / bitmap.width, viewport.height.toFloat() / bitmap.height)
        val x = ((bitmap.width * fit * scale - viewport.width) / 2).coerceAtLeast(0f)
        val y = ((bitmap.height * fit * scale - viewport.height) / 2).coerceAtLeast(0f)
        return Offset(value.x.coerceIn(-x, x), value.y.coerceIn(-y, y))
    }
    fun scale(value: Float) { zoom = value.coerceIn(1f, 4f); pan = bound(pan, zoom) }
    Dialog(model::closePreview, DialogProperties(usePlatformDefaultWidth = false, decorFitsSystemWindows = false)) {
        Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
            Column(Modifier.fillMaxSize().safeDrawingPadding()) {
                Row(Modifier.fillMaxWidth().padding(horizontal = 12.dp), verticalAlignment = Alignment.CenterVertically) {
                    Column(Modifier.weight(1f).padding(vertical = 8.dp)) {
                        Text(row.entry.name, style = MaterialTheme.typography.titleMedium, maxLines = 2, overflow = TextOverflow.Ellipsis)
                        Text("图片预览 · 显示副本", style = MaterialTheme.typography.labelSmall, color = LocalWorkspaceColors.current.muted)
                    }
                    TextButton(model::closePreview) { Text("关闭预览") }
                }
                HorizontalDivider()
                Box(Modifier.weight(1f).fillMaxWidth().clipToBounds().onSizeChanged { viewport = it; pan = bound(pan, zoom) }, contentAlignment = Alignment.Center) {
                    if (bitmap != null) Image(bitmap.asImageBitmap(), "${row.entry.name} 图片预览", Modifier.fillMaxSize()
                        .semantics {
                            stateDescription = "缩放 ${(zoom * 100).toInt()}%"
                            customActions = listOf(
                                CustomAccessibilityAction("向左平移") { pan = bound(pan + Offset(viewport.width / 4f, 0f), zoom); true },
                                CustomAccessibilityAction("向右平移") { pan = bound(pan - Offset(viewport.width / 4f, 0f), zoom); true },
                                CustomAccessibilityAction("向上平移") { pan = bound(pan + Offset(0f, viewport.height / 4f), zoom); true },
                                CustomAccessibilityAction("向下平移") { pan = bound(pan - Offset(0f, viewport.height / 4f), zoom); true },
                            )
                        }
                        .onKeyEvent { event ->
                            if (event.type != KeyEventType.KeyDown || zoom <= 1f) false else {
                                val delta = when (event.key) {
                                    Key.DirectionLeft -> Offset(viewport.width / 4f, 0f)
                                    Key.DirectionRight -> Offset(-viewport.width / 4f, 0f)
                                    Key.DirectionUp -> Offset(0f, viewport.height / 4f)
                                    Key.DirectionDown -> Offset(0f, -viewport.height / 4f)
                                    else -> null
                                }
                                if (delta == null) false else { pan = bound(pan + delta, zoom); true }
                            }
                        }.focusable()
                        .pointerInput(key, bitmap) { detectTransformGestures { _, delta, factor, _ ->
                            zoom = (zoom * factor).coerceIn(1f, 4f); pan = bound(pan + delta, zoom)
                        } }
                        .pointerInput(key, bitmap) { detectTapGestures(onDoubleTap = { scale(if (zoom > 1f) 1f else 2f) }) }
                        .graphicsLayer { scaleX = zoom; scaleY = zoom; translationX = pan.x; translationY = pan.y }, contentScale = ContentScale.Fit)
                    else Column(Modifier.widthIn(max = 480.dp).verticalScroll(rememberScrollState()).padding(24.dp),
                        horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(16.dp)) {
                        if (load?.error == null) {
                            CircularProgressIndicator()
                            Text("正在加载图片…", Modifier.semantics { liveRegion = LiveRegionMode.Polite })
                        } else {
                            Text(requireNotNull(load.error), Modifier.semantics { liveRegion = LiveRegionMode.Polite })
                            Text("文件信息仍可查看，原文件保持不变。", color = LocalWorkspaceColors.current.muted)
                            Button({ model.images.retry(key) }) { Text("重试预览") }
                            TextButton({ model.closePreview(); model.select(row) }) { Text("查看文件信息") }
                        }
                    }
                }
                if (bitmap != null) {
                    HorizontalDivider()
                    Row(Modifier.fillMaxWidth().padding(horizontal = 8.dp), horizontalArrangement = Arrangement.SpaceEvenly, verticalAlignment = Alignment.CenterVertically) {
                        TextButton({ scale(zoom / 1.5f) }, enabled = zoom > 1f) { Text("缩小") }
                        Text("${(zoom * 100).toInt()}%", style = MaterialTheme.typography.labelMedium)
                        TextButton({ scale(zoom * 1.5f) }, enabled = zoom < 4f) { Text("放大") }
                        TextButton({ scale(1f) }) { Text("复位") }
                    }
                }
            }
        }
    }
}
