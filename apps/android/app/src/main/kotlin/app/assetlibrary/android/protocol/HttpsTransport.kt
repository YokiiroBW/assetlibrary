package app.assetlibrary.android.protocol

import android.annotation.SuppressLint
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withTimeout
import kotlinx.coroutines.TimeoutCancellationException
import kotlinx.coroutines.delay
import java.io.ByteArrayOutputStream
import java.io.IOException
import java.net.HttpCookie
import java.net.SocketTimeoutException
import java.net.URL
import java.security.MessageDigest
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import java.util.concurrent.atomic.AtomicReference
import javax.net.ssl.HttpsURLConnection
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLException
import javax.net.ssl.X509TrustManager
import kotlin.coroutines.EmptyCoroutineContext
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

internal data class HttpReply(val status: Int, val bytes: ByteArray, val cookie: String?, val retryAfter: String?) {
    val body: String get() = bytes.toString(Charsets.UTF_8)
}

// ADR-0017 explicitly permits an origin-bound exact leaf pin. This rejects all other
// certificates and expired leaves; HttpsURLConnection separately verifies hostnames.
@SuppressLint("CustomX509TrustManager")
internal class PinnedCertificateTrust(private val expected: String) : X509TrustManager {
    override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
    override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) = throw CertificateException("Client certificates unsupported")
    override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) {
        val leaf = chain.firstOrNull() ?: throw CertificateException("Missing leaf certificate")
        leaf.checkValidity()
        val actual = MessageDigest.getInstance("SHA-256").digest(leaf.encoded).joinToString("") { "%02x".format(it) }
        if (!MessageDigest.isEqual(actual.toByteArray(), expected.toByteArray())) throw CertificateException("Certificate pin mismatch")
    }
}

internal class HttpsTransport(private val profile: ServerProfile) {
    private val pinnedFactory = if (profile.certificatePin.isEmpty()) null else SSLContext.getInstance("TLS").apply {
        init(null, arrayOf(PinnedCertificateTrust(profile.certificatePin)), null)
    }.socketFactory

    suspend fun request(path: String, method: String, body: String?, cookie: String?, csrf: String?): HttpReply = try {
        withTimeout(5_000) { execute(path, method, body, cookie, csrf, MAX_BODY, 5_000, false) }
    } catch (_: TimeoutCancellationException) { throw ApiFailure(504, "timeout") }

    suspend fun image(path: String, cookie: String, variant: ImageVariant): HttpReply = try {
        withTimeout(20_000) {
            var attempts = 0
            var reply = execute(path, "GET", null, cookie, null, variant.maxBytes, 20_000, true)
            while (reply.status == 429 && attempts++ < 2) {
                val seconds = reply.retryAfter?.toLongOrNull()
                val wait = if (seconds != null) seconds.coerceIn(0, 20) * 1000 else try {
                    java.time.Duration.between(java.time.Instant.now(), java.time.ZonedDateTime.parse(
                        reply.retryAfter, java.time.format.DateTimeFormatter.RFC_1123_DATE_TIME).toInstant()).toMillis().coerceIn(0, 20_000)
                } catch (_: Exception) { 1000L }
                delay(wait)
                reply = execute(path, "GET", null, cookie, null, variant.maxBytes, 20_000, true)
            }
            reply
        }
    } catch (_: TimeoutCancellationException) { throw ApiFailure(504, "preview_timeout") }

    private suspend fun execute(path: String, method: String, body: String?, cookie: String?, csrf: String?, maxBody: Int, timeout: Int, image: Boolean): HttpReply =
        suspendCancellableCoroutine { continuation ->
            val active = AtomicReference<HttpsURLConnection?>()
            continuation.invokeOnCancellation { active.getAndSet(null)?.disconnect() }
            Dispatchers.IO.dispatch(EmptyCoroutineContext) {
                try {
                    if (!continuation.isActive) return@dispatch
                    val connection = URL(profile.origin + path).openConnection() as HttpsURLConnection
                    active.set(connection)
                    try {
                        if (!continuation.isActive) return@dispatch
                        connection.instanceFollowRedirects = false
                        connection.connectTimeout = timeout
                        connection.readTimeout = timeout
                        connection.useCaches = false
                        connection.requestMethod = method
                        connection.setRequestProperty("Origin", profile.origin)
                        connection.setRequestProperty("Accept", if (image) "image/png" else "application/json")
                        connection.setRequestProperty("Accept-Encoding", "identity")
                        connection.setRequestProperty("Content-Type", "application/json")
                        cookie?.let { connection.setRequestProperty("Cookie", it) }
                        csrf?.let { connection.setRequestProperty("X-AssetLibrary-CSRF", it) }
                        pinnedFactory?.let { connection.sslSocketFactory = it }
                        // The platform hostname verifier remains enabled even for an explicitly pinned leaf.
                        if (body != null) {
                            val payload = body.toByteArray(Charsets.UTF_8)
                            connection.doOutput = true
                            connection.setFixedLengthStreamingMode(payload.size)
                            connection.connect()
                            connection.outputStream.use { it.write(payload) }
                        }
                        val status = connection.responseCode
                        if (status in 300..399) throw ApiFailure(status, "redirect_refused")
                        if (status == 401 || status == 403 || status == 404) throw ApiFailure(status, "access_rejected")
                        val declared = connection.contentLengthLong
                        val limit = if (image && status != 200) 16 * 1024 else maxBody
                        if (declared > limit) throw ApiFailure(status, "response_too_large")
                        if (image && status == 200 && (declared <= 0 ||
                            connection.contentType?.substringBefore(';')?.trim()?.lowercase() != "image/png" ||
                            connection.contentEncoding?.lowercase()?.let { it != "identity" } == true)) throw ApiFailure(0, "preview_invalid")
                        val input = if (status >= 400) connection.errorStream else connection.inputStream
                        val output = ByteArrayOutputStream()
                        input?.use {
                            val buffer = ByteArray(8192)
                            while (continuation.isActive) {
                                val read = it.read(buffer)
                                if (read < 0) break
                                if (output.size() + read > limit) throw ApiFailure(status, "response_too_large")
                                output.write(buffer, 0, read)
                            }
                        }
                        if (image && status == 200 && output.size().toLong() != declared) throw ApiFailure(0, "preview_invalid")
                        val receivedCookie = connection.headerFields.entries.filter { it.key.equals("Set-Cookie", true) }
                            .flatMap { it.value }.mapNotNull(::sessionCookie).singleOrNull()
                        if (continuation.isActive) continuation.resume(HttpReply(status, output.toByteArray(), receivedCookie, connection.getHeaderField("Retry-After")))
                    } finally { active.getAndSet(null)?.disconnect() }
                } catch (error: Exception) {
                    val failure = when (error) {
                        is ApiFailure -> error
                        is SSLException -> ApiFailure(0, "tls")
                        is SocketTimeoutException -> ApiFailure(0, "timeout")
                        is IOException -> ApiFailure(0, "network")
                        else -> ApiFailure(0, "invalid_response")
                    }
                    if (continuation.isActive) continuation.resumeWithException(failure)
                }
            }
        }

    private fun sessionCookie(header: String): String? {
        if (header.length > 4096) return null
        val parsed = HttpCookie.parse(header).singleOrNull() ?: return null
        if (parsed.name != "__Host-AssetLibrary-Session" || !parsed.secure || !parsed.isHttpOnly ||
            parsed.domain != null || parsed.path != "/" || parsed.hasExpired() || parsed.value.isBlank()) return null
        return "${parsed.name}=${parsed.value}"
    }

    companion object { private const val MAX_BODY = 2 * 1024 * 1024 }
}
