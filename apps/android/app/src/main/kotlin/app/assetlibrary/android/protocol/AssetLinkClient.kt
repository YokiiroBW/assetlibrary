package app.assetlibrary.android.protocol

import app.assetlibrary.assetlink.AssetLinkCodec
import app.assetlibrary.assetlink.ControlRequestMessage
import app.assetlibrary.assetlink.ControlResultMessage
import app.assetlibrary.assetlink.ErrorMessage
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.long
import java.time.Instant
import java.util.UUID

class AssetLinkClient(profile: ServerProfile) : AssetApi {
    private val transport = HttpsTransport(profile)
    private var cookie: String? = null
    private var csrf: String? = null
    private var principal: String? = null
    private var sessionGeneration = 0L

    override suspend fun signIn(account: String, password: String): Session = decode {
        clearSession()
        val reply = transport.request("/assetlink/v1/auth/login", "POST", buildJsonObject {
            put("account_name", account); put("password", password)
        }.toString(), null, null)
        val result = Json.parseToJsonElement(reply.body) as JsonObject
        if (reply.status !in 200..299) throw ApiFailure(reply.status, result.text("code"))
        require(result.flag("authenticated"))
        val token = result.text("csrf_token").also { require(it.matches(Regex("[A-Za-z0-9_-]{43}"))) }
        val session = parseSession(result)
        require(session.expiresAt > Instant.now())
        cookie = reply.cookie ?: throw ApiFailure(0, "session_cookie_required")
        csrf = token
        principal = session.principalId
        session
    }

    override suspend fun validateSession(): Session = decode {
        if (cookie == null || principal == null) throw ApiFailure(401, "authentication_required")
        val reply = transport.request("/assetlink/v1/auth/session", "GET", null, cookie, null)
        require(reply.status == 200)
        val body = Json.parseToJsonElement(reply.body) as JsonObject
        val session = parseSession(body)
        if (session.principalId != principal || session.expiresAt <= Instant.now()) throw ApiFailure(401, "identity_changed")
        csrf = body.text("csrf_token").also { require(it.matches(Regex("[A-Za-z0-9_-]{43}"))) }
        session
    }

    private fun parseSession(body: JsonObject): Session {
        require(body.flag("authenticated"))
        return Session(body.text("display_name"), body.flag("is_system_administrator"), Instant.parse(body.text("absolute_expires_at")),
            body.text("principal_id", 128).also { require(it.isNotBlank()) })
    }

    override suspend fun signOut() {
        try {
            val reply = transport.request("/assetlink/v1/auth/logout", "POST", "{}", cookie, csrf)
            if (reply.status != 204) throw ApiFailure(reply.status, "logout_unconfirmed")
        } finally { clearSession() }
    }

    override fun clearSession() { sessionGeneration++; cookie = null; csrf = null; principal = null }

    override suspend fun image(libraryId: String, entryId: String, variant: ImageVariant): ImagePayload {
        val generation = sessionGeneration
        return decode {
            fun checkedId(value: String): String {
                require(value.matches(Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")))
                require(UUID.fromString(value) != UUID(0, 0))
                return value
            }
            val path = "/assetlink/v1/libraries/${checkedId(libraryId)}/entries/${checkedId(entryId)}/image?variant=${variant.wire}"
            val currentCookie = cookie ?: throw ApiFailure(401, "authentication_required")
            try {
                val reply = transport.image(path, currentCookie, variant)
                if (generation != sessionGeneration) throw CancellationException("Obsolete image session")
                if (reply.status != 200) {
                    val code = try { (Json.parseToJsonElement(reply.body) as JsonObject).text("code", 128) }
                        catch (_: Exception) { "preview_invalid" }
                    throw ApiFailure(reply.status, code)
                }
                withContext(Dispatchers.Default) { ImagePayload.parse(reply.bytes, variant) }
            } catch (error: Exception) {
                if (generation != sessionGeneration) throw CancellationException("Obsolete image session")
                throw error
            }
        }
    }

    override suspend fun libraries(cursor: String?, category: String?): Page<Library> = decode {
        val result = control("libraries.list", page(cursor) { category?.let { put("category", it) } })
        Page(result.items().map { it.library() }, result.optionalText("next_cursor"))
    }

    override suspend fun browse(library: Library, path: String, cursor: String?, options: BrowseOptions, anchor: String?): Page<AssetRow> = decode {
        val result = control("entries.browse", page(cursor) {
            put("library_id", library.id); put("parent_relative_path", path)
            put("sort_by", options.sort); put("sort_direction", options.direction)
            put("kind", options.kind); put("name_filter", options.filter)
            if (cursor == null) anchor?.let { put("anchor_entry_id", it) }
        })
        val responseLibrary = result.obj("library").library()
        require(responseLibrary.id == library.id && result.text("parent_relative_path") == path)
        Page(result.items().map { AssetRow(responseLibrary, it.entry().also { entry ->
            require(entry.libraryId == library.id && entry.path.substringBeforeLast('/', "") == path)
        }) }, result.optionalText("next_cursor"))
    }

    override suspend fun search(query: String, scope: SearchScope, cursor: String?): Page<AssetRow> = decode {
        val result = control("assets.search", page(cursor) {
            put("query", query); put("scope", scope.kind)
            scope.libraryId?.let { put("library_id", it) }
            if (scope.kind == "directory") put("parent_relative_path", scope.path)
        })
        Page(result.items().map {
            val library = it.obj("library").library()
            val entry = it.obj("entry").entry()
            require(entry.libraryId == library.id)
            if (scope.kind != "all") require(library.id == scope.libraryId)
            if (scope.kind == "directory" && scope.path.isNotEmpty()) require(entry.path.startsWith(scope.path + "/"))
            AssetRow(library, entry)
        }, result.optionalText("next_cursor"))
    }

    override suspend fun detail(libraryId: String, entryId: String): AssetRow = decode {
        val body = control("entries.get", buildJsonObject { put("library_id", libraryId); put("entry_id", entryId) })
        val library = body.obj("library").library()
        val entry = body.obj("entry").entry()
        require(library.id == libraryId && entry.libraryId == libraryId && entry.id == entryId)
        AssetRow(library, entry)
    }

    override suspend fun scan(libraryId: String): Scan? = decode {
        val body = control("library_scans.get", buildJsonObject { put("library_id", libraryId) })
        require(body.text("library_id") == libraryId)
        if (body["scan"] == JsonNull) return@decode null
        val value = body.obj("scan")
        fun count(name: String): String {
            val number = value[name] as JsonPrimitive
            require(!number.isString && number.long >= 0)
            return number.long.toString()
        }
        Scan(value.text("state").also { require(it in listOf("queued", "leased", "succeeded", "failed", "cancelled")) }, count("observed_entries"), count("committed_entries"))
    }

    private suspend fun control(operation: String, body: JsonObject): JsonObject {
        if (cookie == null || csrf == null) throw ApiFailure(401, "authentication_required")
        val id = UUID.randomUUID().toString()
        val request = ControlRequestMessage(buildJsonObject {
            put("message_type", "control.request"); put("request_id", id)
            put("operation", operation); put("body", body); put("timeout_ms", 5000)
        })
        val response = transport.request("/assetlink/v1/control", "POST", request.encode(), cookie, csrf)
        val message = AssetLinkCodec.parse(response.body)
        require(message.raw.text("request_id") == id)
        if (message is ErrorMessage) throw ApiFailure(response.status, message.raw.obj("error").text("code"))
        require(response.status in 200..299 && message is ControlResultMessage && message.ok)
        return message.body
    }

    private suspend fun <T> decode(action: suspend () -> T): T = try { action() } catch (error: Exception) {
        when (error) {
            is CancellationException -> throw error
            is ApiFailure -> { if (error.status == 401 || error.status == 403) clearSession(); throw error }
            else -> throw ApiFailure(0, "invalid_response")
        }
    }

    private fun page(cursor: String?, block: kotlinx.serialization.json.JsonObjectBuilder.() -> Unit): JsonObject = buildJsonObject {
        put("page_size", 100); cursor?.let { put("cursor", it) }; block()
    }
}
