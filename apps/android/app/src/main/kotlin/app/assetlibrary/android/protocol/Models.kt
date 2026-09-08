package app.assetlibrary.android.protocol

import app.assetlibrary.assetlink.AssetLinkUInt64
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.boolean
import java.net.URI
import java.time.Instant

data class ServerProfile(val origin: String, val certificatePin: String) {
    companion object {
        fun parse(address: String, fingerprint: String): ServerProfile {
            val uri = URI(address.trim())
            require(uri.scheme.equals("https", true) && !uri.host.isNullOrBlank())
            require(uri.rawUserInfo == null && uri.rawQuery == null && uri.rawFragment == null)
            require(uri.rawPath.isNullOrEmpty() || uri.rawPath == "/")
            require(uri.port == -1 || uri.port in 1..65535)
            val pin = fingerprint.trim().replace(":", "").lowercase()
            require(pin.isEmpty() || pin.matches(Regex("[0-9a-f]{64}")))
            return ServerProfile("https://${uri.host.lowercase()}${if (uri.port == -1) "" else ":${uri.port}"}", pin)
        }
    }
}

data class Session(val displayName: String, val administrator: Boolean, val expiresAt: Instant, val principalId: String = "")
data class Library(val id: String, val name: String, val availability: String, val access: String, val category: String)
data class Entry(
    val id: String, val libraryId: String, val path: String, val name: String,
    val kind: String, val bytes: String?, val modified: String,
) { val directory: Boolean get() = kind == "directory" }
data class AssetRow(val library: Library, val entry: Entry)
data class Page<T>(val items: List<T>, val next: String?)
data class BrowseOptions(val sort: String = "name", val direction: String = "asc", val kind: String = "all", val filter: String = "")
data class SearchScope(val kind: String = "all", val libraryId: String? = null, val path: String = "")
data class Scan(val state: String, val observed: String, val committed: String)

class ApiFailure(val status: Int, val code: String) : Exception(code) {
    val userMessage: String get() = when {
        status == 401 -> "登录已失效，请重新登录。"
        status == 403 -> "访问被拒绝，已清除当前内容。"
        code == "invalid_credentials" -> "账号或口令不正确。"
        code == "tls" -> "证书验证失败。请检查服务器名称、有效期与证书指纹。"
        code == "timeout" -> "连接超时，请检查网络后重试。"
        code == "network" -> "无法连接服务器，请检查网络后重试。"
        status == 429 -> "请求过于频繁，请稍后重试。"
        status == 404 -> "条目已不可用，请刷新目录。"
        code == "invalid_cursor" -> "目录已变化，请刷新后重新分页。"
        else -> "服务器未返回有效结果，请重试。"
    }
}

internal fun JsonObject.text(key: String, max: Int = 4096): String {
    val value = this[key] as? JsonPrimitive ?: error("Invalid response")
    require(value.isString && value.content.length <= max)
    return value.content
}
internal fun JsonObject.optionalText(key: String): String? =
    if (this[key] == null || this[key] == JsonNull) null else text(key)
internal fun JsonObject.flag(key: String): Boolean = (this[key] as JsonPrimitive).also { require(!it.isString) }.boolean
internal fun JsonObject.obj(key: String): JsonObject = this[key] as JsonObject
internal fun JsonObject.items(): List<JsonObject> = (this["items"] as JsonArray).also { require(it.size <= 100) }.map { it as JsonObject }
internal fun JsonElement.library(): Library {
    val value = this as JsonObject
    val category = value.optionalText("category") ?: "general"
    require(category in categories.keys)
    return Library(value.text("library_id", 128), value.text("display_name", 1024), value.text("availability").also {
        require(it in listOf("online", "offline"))
    }, value.text("access_level").also { require(it in listOf("read_only", "read_write", "organize", "library_administrator")) }, category)
}
internal fun JsonElement.entry(): Entry {
    val value = this as JsonObject
    val bytes = value.optionalText("content_length")?.also { AssetLinkUInt64.parse(it) }
    val modified = value.text("last_write_time_utc").also { Instant.parse(it) }
    return Entry(value.text("entry_id", 128), value.text("library_id", 128), value.text("relative_path"),
        value.text("name"), value.text("kind").also { require(it in listOf("file", "directory", "reparse_file", "reparse_directory")) }, bytes, modified)
}

val categories: Map<String, String> = linkedMapOf("photos" to "照片", "images" to "图片", "videos" to "视频", "music" to "音乐",
    "projects" to "工程", "documents" to "文档与阅读", "characters" to "角色", "general" to "通用")

interface AssetApi {
    suspend fun signIn(account: String, password: String): Session
    suspend fun validateSession(): Session
    suspend fun signOut()
    fun clearSession()
    suspend fun libraries(cursor: String?, category: String?): Page<Library>
    suspend fun browse(library: Library, path: String, cursor: String?, options: BrowseOptions, anchor: String? = null): Page<AssetRow>
    suspend fun search(query: String, scope: SearchScope, cursor: String?): Page<AssetRow>
    suspend fun detail(libraryId: String, entryId: String): AssetRow
    suspend fun scan(libraryId: String): Scan?
}
