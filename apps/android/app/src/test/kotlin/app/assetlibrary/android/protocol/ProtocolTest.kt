package app.assetlibrary.android.protocol

import com.sun.net.httpserver.HttpsConfigurator
import com.sun.net.httpserver.HttpsServer
import kotlinx.coroutines.async
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.delay
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import org.junit.After
import org.junit.Assert.*
import org.junit.Before
import org.junit.Test
import org.junit.Rule
import org.junit.rules.TestName
import java.net.InetSocketAddress
import java.nio.file.Files
import java.security.KeyStore
import java.security.MessageDigest
import java.time.Instant
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicInteger
import javax.net.ssl.KeyManagerFactory
import javax.net.ssl.SSLContext

class ProtocolTest {
    @get:Rule val testName = TestName()
    private lateinit var server: HttpsServer
    private lateinit var profile: ServerProfile
    private lateinit var executor: java.util.concurrent.ExecutorService
    private val controls = AtomicInteger()
    private val logins = AtomicInteger()
    private var mode = "valid"
    private val libraryJson = """{"library_id":"lib-1","display_name":"样例库","availability":"online","access_level":"read_only","category":"images"}"""

    @Before fun setup() {
        val directory = Files.createTempDirectory("assetlibrary-android-tls-")
        val keystore = directory.resolve("localhost.p12")
        val keytool = java.nio.file.Path.of(System.getProperty("java.home"), "bin", if (System.getProperty("os.name").orEmpty().startsWith("Windows")) "keytool.exe" else "keytool")
        val arguments = mutableListOf(keytool.toString(), "-genkeypair", "-alias", "local", "-keyalg", "RSA", "-keysize", "2048",
            "-validity", "1", "-dname", "CN=localhost", "-ext", "SAN=dns:localhost", "-storetype", "PKCS12", "-keystore", keystore.toString(),
            "-storepass", "fixture-only", "-keypass", "fixture-only", "-noprompt")
        if (testName.methodName.contains("expired")) arguments.addAll(listOf("-startdate", "-2d"))
        val process = ProcessBuilder(arguments).redirectErrorStream(true).start()
        process.inputStream.readAllBytes()
        assertEquals(0, process.waitFor())
        val store = KeyStore.getInstance("PKCS12").apply { Files.newInputStream(keystore).use { load(it, "fixture-only".toCharArray()) } }
        val pin = MessageDigest.getInstance("SHA-256").digest(store.getCertificate("local").encoded).joinToString("") { "%02x".format(it) }
        val keys = KeyManagerFactory.getInstance(KeyManagerFactory.getDefaultAlgorithm()).apply { init(store, "fixture-only".toCharArray()) }
        val ssl = SSLContext.getInstance("TLS").apply { init(keys.keyManagers, null, null) }
        Files.delete(keystore); Files.delete(directory)
        server = HttpsServer.create(InetSocketAddress("localhost", 0), 0)
        server.httpsConfigurator = HttpsConfigurator(ssl)
        executor = Executors.newFixedThreadPool(2)
        server.executor = executor
        profile = ServerProfile.parse("https://localhost:${server.address.port}", pin)
        server.createContext("/") { exchange ->
            exchange.use {
                assertEquals(profile.origin, exchange.requestHeaders.getFirst("Origin"))
                when (exchange.requestURI.path) {
                    "/assetlink/v1/auth/login" -> {
                        logins.incrementAndGet()
                        val payload = Json.parseToJsonElement(exchange.requestBody.readAllBytes().toString(Charsets.UTF_8)) as JsonObject
                        assertEquals("sample", (payload["account_name"] as JsonPrimitive).content)
                        exchange.responseHeaders.add("Set-Cookie", "__Host-AssetLibrary-Session=synthetic; Path=/; Secure; HttpOnly; SameSite=Strict")
                        reply(exchange, 200, """{"authenticated":true,"display_name":"样例账号","principal_id":"test","is_system_administrator":false,"csrf_token":"${"x".repeat(43)}","absolute_expires_at":"${Instant.now().plusSeconds(3600)}"}""")
                    }
                    "/assetlink/v1/auth/logout" -> reply(exchange, 500, "{}")
                    else -> {
                        controls.incrementAndGet()
                        assertEquals("__Host-AssetLibrary-Session=synthetic", exchange.requestHeaders.getFirst("Cookie"))
                        assertEquals("x".repeat(43), exchange.requestHeaders.getFirst("X-AssetLibrary-CSRF"))
                        val request = Json.parseToJsonElement(exchange.requestBody.readAllBytes().toString(Charsets.UTF_8)) as JsonObject
                        if (mode in listOf("401", "403", "404")) {
                            exchange.sendResponseHeaders(mode.toInt(), 4096)
                            Thread.sleep(1500)
                            return@createContext
                        }
                        if (mode == "redirect") { exchange.responseHeaders.add("Location", "https://elsewhere.invalid/"); reply(exchange, 302, "{}"); return@createContext }
                        if (mode == "huge") { reply(exchange, 200, "x".repeat(2 * 1024 * 1024 + 1)); return@createContext }
                        if (mode == "slow") { exchange.sendResponseHeaders(200, 100); Thread.sleep(1500); return@createContext }
                        val items = if (mode == "too_many") List(101) { libraryJson }.joinToString(",") else libraryJson
                        val outside = """{"entry_id":"escape","library_id":"lib-1","relative_path":"outside/leak.txt","name":"leak.txt","kind":"file","content_length":"10","last_write_time_utc":"2026-09-08T00:00:00Z"}"""
                        val responseBody = when (mode) {
                            "search_outside" -> """{"items":[{"library":$libraryJson,"entry":$outside,"hit_reason":"name"}],"next_cursor":null}"""
                            "browse_outside" -> """{"library":$libraryJson,"parent_relative_path":"inside","items":[$outside],"next_cursor":null}"""
                            else -> """{"items":[$items],"next_cursor":null}"""
                        }
                        val result = buildJsonObject {
                            put("message_type", if (mode == "unknown") "future.message" else "control.result")
                            put("request_id", if (mode == "mismatch") JsonPrimitive("wrong") else request["request_id"]!!)
                            put("ok", true); put("body", Json.parseToJsonElement(responseBody))
                        }
                        reply(exchange, 200, result.toString())
                    }
                }
            }
        }
        server.start()
    }

    @After fun teardown() { server.stop(0); executor.shutdownNow() }
    private fun reply(exchange: com.sun.net.httpserver.HttpExchange, status: Int, text: String) {
        val bytes = text.toByteArray()
        exchange.responseHeaders.add("Content-Type", "application/json")
        exchange.sendResponseHeaders(status, bytes.size.toLong())
        exchange.responseBody.write(bytes)
    }
    private suspend fun loggedIn(): AssetLinkClient = AssetLinkClient(profile).also { it.signIn("sample", "fixture") }
    private suspend fun failure(block: suspend () -> Unit): ApiFailure = try { block(); error("Expected failure") } catch (error: ApiFailure) { error }

    @Test fun `origin requires HTTPS root and exact valid pin`() {
        listOf("http://localhost", "https://user:password@localhost", "https://localhost/path", "https://localhost?q=1", "https://localhost#x", "https://localhost:0").forEach {
            assertThrows(Exception::class.java) { ServerProfile.parse(it, "") }
        }
        assertThrows(Exception::class.java) { ServerProfile.parse("https://localhost", "abc") }
        assertEquals("https://localhost:5443", ServerProfile.parse(" https://LOCALHOST:5443/ ", "").origin)
    }
    @Test fun `pinned TLS login and generated control envelope round trip`() = runBlocking {
        assertEquals("样例库", loggedIn().libraries(null, null).items.single().name)
        assertEquals(1, controls.get())
    }
    @Test fun `untrusted leaf rejected before sending credentials`() = runBlocking {
        assertEquals("tls", failure { AssetLinkClient(profile.copy(certificatePin = "")).signIn("sample", "fixture") }.code)
        assertEquals(0, logins.get())
    }
    @Test fun `wrong certificate pin rejected before sending credentials`() = runBlocking {
        assertEquals("tls", failure { AssetLinkClient(profile.copy(certificatePin = "a".repeat(64))).signIn("sample", "fixture") }.code)
        assertEquals(0, logins.get())
    }
    @Test fun `pin does not bypass hostname verification`() = runBlocking {
        assertEquals("tls", failure { AssetLinkClient(profile.copy(origin = profile.origin.replace("localhost", "127.0.0.1"))).signIn("sample", "fixture") }.code)
        assertEquals(0, logins.get())
    }
    @Test fun `expired pinned certificate rejected before sending credentials`() = runBlocking {
        assertEquals("tls", failure { AssetLinkClient(profile).signIn("sample", "fixture") }.code)
        assertEquals(0, logins.get())
    }
    @Test fun `same library directory scope and direct parent escapes rejected`() = runBlocking {
        val client = loggedIn()
        mode = "search_outside"
        assertEquals("invalid_response", failure { client.search("leak", SearchScope("directory", "lib-1", "inside"), null) }.code)
        mode = "browse_outside"
        assertEquals("invalid_response", failure { client.browse(Json.parseToJsonElement(libraryJson).library(), "inside", null, BrowseOptions()) }.code)
    }
    @Test fun `authorization status wins over stalled malformed body`() = runBlocking {
        for (status in listOf("401", "403", "404")) {
            mode = "valid"
            val client = loggedIn()
            mode = status
            val started = System.nanoTime()
            assertEquals(status.toInt(), failure { client.libraries(null, null) }.status)
            assertTrue((System.nanoTime() - started) / 1_000_000 < 1200)
            if (status != "404") assertEquals(401, failure { client.libraries(null, null) }.status)
        }
    }
    @Test fun `unknown envelope mismatched request and overlarge page fail closed`() = runBlocking {
        val client = loggedIn()
        for (case in listOf("unknown", "mismatch", "too_many", "huge")) {
            mode = case
            assertNotNull(failure { client.libraries(null, null) })
        }
    }
    @Test fun `cross origin redirect is refused`() = runBlocking {
        val client = loggedIn(); mode = "redirect"
        assertEquals("redirect_refused", failure { client.libraries(null, null) }.code)
    }
    @Test fun `request cancellation interrupts body read`() = runBlocking {
        val client = loggedIn(); mode = "slow"
        val task = async { client.libraries(null, null) }
        delay(100); task.cancelAndJoin()
        assertTrue(task.isCancelled)
    }
    @Test fun `logout failure still clears local credentials`() = runBlocking {
        val client = loggedIn()
        assertEquals(500, failure { client.signOut() }.status)
        assertEquals(401, failure { client.libraries(null, null) }.status)
    }
    @Test fun `wire integers preserve uint64 and reject overflow`() {
        val valid = """{"entry_id":"one","library_id":"lib-1","relative_path":"file","name":"file","kind":"file","content_length":"18446744073709551615","last_write_time_utc":"2026-09-08T00:00:00Z"}"""
        assertEquals("18446744073709551615", Json.parseToJsonElement(valid).entry().bytes)
        assertThrows(Exception::class.java) { Json.parseToJsonElement(valid.replace("18446744073709551615", "18446744073709551616")).entry() }
    }
}
