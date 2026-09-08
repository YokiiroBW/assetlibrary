package app.assetlibrary.android.protocol

import android.annotation.SuppressLint
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withTimeout
import kotlinx.coroutines.TimeoutCancellationException
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

internal data class HttpReply(val status: Int, val body: String, val cookie: String?)

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
        withTimeout(5_000) { execute(path, method, body, cookie, csrf) }
    } catch (_: TimeoutCancellationException) { throw ApiFailure(504, "timeout") }

    private suspend fun execute(path: String, method: String, body: String?, cookie: String?, csrf: String?): HttpReply =
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
                        connection.connectTimeout = 5_000
                        connection.readTimeout = 5_000
                        connection.useCaches = false
                        connection.requestMethod = method
                        connection.setRequestProperty("Origin", profile.origin)
                        connection.setRequestProperty("Accept", "application/json")
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
                        if (declared > MAX_BODY) throw ApiFailure(status, "response_too_large")
                        val input = if (status >= 400) connection.errorStream else connection.inputStream
                        val output = ByteArrayOutputStream()
                        input?.use {
                            val buffer = ByteArray(8192)
                            while (continuation.isActive) {
                                val read = it.read(buffer)
                                if (read < 0) break
                                if (output.size() + read > MAX_BODY) throw ApiFailure(status, "response_too_large")
                                output.write(buffer, 0, read)
                            }
                        }
                        val receivedCookie = connection.headerFields.entries.filter { it.key.equals("Set-Cookie", true) }
                            .flatMap { it.value }.mapNotNull(::sessionCookie).singleOrNull()
                        if (continuation.isActive) continuation.resume(HttpReply(status, output.toString(Charsets.UTF_8.name()), receivedCookie))
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
