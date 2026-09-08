package app.assetlibrary.android

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.core.content.edit
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewmodel.CreationExtras
import androidx.lifecycle.viewmodel.compose.viewModel
import app.assetlibrary.android.protocol.ServerProfile
import app.assetlibrary.android.ui.WorkspaceApp
import app.assetlibrary.android.ui.WorkspaceTheme
import app.assetlibrary.android.workspace.WorkspaceModel

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        val preferences = getSharedPreferences("server-profile", MODE_PRIVATE)
        val stored = preferences.getString("origin", null)?.let { address ->
            try { ServerProfile.parse(address, preferences.getString("pin", "") ?: "") } catch (_: Exception) { null }
        }
        val factory = object : ViewModelProvider.Factory {
            override fun <T : ViewModel> create(modelClass: Class<T>, extras: CreationExtras): T {
                require(modelClass.isAssignableFrom(WorkspaceModel::class.java))
                @Suppress("UNCHECKED_CAST")
                return WorkspaceModel(stored, saveProfile = { profile ->
                    preferences.edit { putString("origin", profile.origin); putString("pin", profile.certificatePin) }
                }) as T
            }
        }
        setContent {
            WorkspaceTheme { val model: WorkspaceModel = viewModel(factory = factory); WorkspaceApp(model) }
        }
    }
}
