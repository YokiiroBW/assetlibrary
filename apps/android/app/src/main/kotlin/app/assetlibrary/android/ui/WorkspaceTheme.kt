package app.assetlibrary.android.ui

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.remember
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.core.graphics.toColorInt
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive

data class WorkspaceColors(val folder: Color, val line: Color, val muted: Color)
val LocalWorkspaceColors = staticCompositionLocalOf<WorkspaceColors> { error("Workspace theme is required") }

@Composable
fun WorkspaceTheme(content: @Composable () -> Unit) {
    val context = LocalContext.current
    val dark = isSystemInDarkTheme()
    val tokens = remember(context) {
        context.assets.open("workspace-theme.json").bufferedReader().use { Json.parseToJsonElement(it.readText()).jsonObject["themes"]!!.jsonObject }
    }
    val selected = tokens[if (dark) "dark" else "light"]!!.jsonObject
    fun color(name: String) = Color(selected[name]!!.jsonPrimitive.content.toColorInt())
    val base = if (dark) darkColorScheme() else lightColorScheme()
    val scheme = base.copy(primary = color("accent"), onPrimary = color("on-accent"), primaryContainer = color("accent-soft"),
        onPrimaryContainer = color("ink"), background = color("surface"), onBackground = color("ink"),
        surface = color("surface-strong"), onSurface = color("ink"), surfaceVariant = color("surface"),
        onSurfaceVariant = color("muted"), outline = color("line"), outlineVariant = color("line"),
        error = color("danger"), secondaryContainer = color("accent-soft"), onSecondaryContainer = color("accent"),
        surfaceContainerLowest = color("surface-strong"), surfaceContainerLow = color("surface"),
        surfaceContainer = color("surface-strong"), surfaceContainerHigh = color("surface"), surfaceContainerHighest = color("surface"))
    CompositionLocalProvider(LocalWorkspaceColors provides WorkspaceColors(color("folder"), color("line"), color("muted"))) {
        MaterialTheme(colorScheme = scheme, content = content)
    }
}
