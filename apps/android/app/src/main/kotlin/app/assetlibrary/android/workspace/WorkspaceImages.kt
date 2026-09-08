package app.assetlibrary.android.workspace

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import app.assetlibrary.android.protocol.ApiFailure
import app.assetlibrary.android.protocol.AssetRow
import app.assetlibrary.android.protocol.ImagePayload
import app.assetlibrary.android.protocol.ImageVariant
import app.assetlibrary.android.protocol.imageMessage
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeout
import kotlinx.coroutines.TimeoutCancellationException
import java.security.MessageDigest

data class ImageKey(val library: String, val entry: String, val variant: ImageVariant) {
    companion object { fun of(row: AssetRow, variant: ImageVariant) = ImageKey(row.library.id, row.entry.id, variant) }
}

data class ImageLoad(val bitmap: Bitmap? = null, val error: String? = null) {
    val loading: Boolean get() = bitmap == null && error == null
}

/** Owns only ephemeral presentation data. Each visible admission still asks the server. */
class WorkspaceImages(
    private val scope: CoroutineScope,
    private val fetch: suspend (ImageKey) -> ImagePayload,
    private val rejected: (ApiFailure) -> Unit,
) {
    private val mutable = MutableStateFlow<Map<ImageKey, ImageLoad>>(emptyMap())
    val state = mutable.asStateFlow()
    private val jobs = mutableMapOf<ImageKey, Job>()
    private val permits = Semaphore(2)
    private val cache = LinkedHashMap<String, Bitmap>(16, 0.75f, true)
    private var cacheBytes = 0
    private var generation = 0L
    private var enabled = false
    private var visible: List<AssetRow> = emptyList()
    private var preview: AssetRow? = null

    fun resume() { enabled = true }
    fun pause() { enabled = false; clear() }

    fun clear() {
        generation++
        val obsolete = jobs.values.toList()
        jobs.clear()
        obsolete.forEach { it.cancel() }
        mutable.value = emptyMap()
        visible = emptyList(); preview = null
        // Drop owning references. Bitmap.recycle while Compose is drawing can crash RenderThread.
        cache.clear(); cacheBytes = 0
    }

    fun visible(rows: List<AssetRow>) {
        if (!enabled) return
        visible = rows.filter { it.entry.kind == "file" }.distinctBy { it.library.id to it.entry.id }.take(MAX_VISIBLE)
        reconcile()
    }

    fun preview(row: AssetRow?) {
        if (preview != row) {
            val entries = cache.entries.iterator()
            while (entries.hasNext()) {
                val entry = entries.next()
                if (entry.key.endsWith(ImageVariant.PREVIEW.wire)) { cacheBytes -= entry.value.allocationByteCount; entries.remove() }
            }
        }
        preview = row; reconcile()
    }

    fun refresh() {
        val rows = visible
        val selected = preview
        clear()
        visible = rows; preview = selected
        reconcile()
    }

    fun retry(key: ImageKey) {
        if (!enabled || key !in desired()) return
        jobs.remove(key)?.cancel()
        mutable.value = mutable.value - key
        reconcile()
    }

    private fun desired(): Set<ImageKey> = if (!enabled) emptySet() else preview?.let {
        setOf(ImageKey.of(it, ImageVariant.PREVIEW))
    } ?: visible.map { ImageKey.of(it, ImageVariant.THUMBNAIL) }.toSet()

    private fun reconcile() {
        val desired = desired()
        (jobs.keys - desired).forEach { jobs.remove(it)?.cancel() }
        mutable.value = mutable.value.filterKeys { it in desired }
        for (key in desired) if (enabled && key in desired() && key !in mutable.value) load(key)
    }

    private fun load(key: ImageKey) {
        val currentGeneration = generation
        mutable.value = mutable.value + (key to ImageLoad())
        val job = scope.launch(start = CoroutineStart.LAZY) {
            try {
                withTimeout(20_000) { permits.withPermit {
                    // Even a decoded cache hit requires fresh authorized, source-checked bytes.
                    val payload = fetch(key)
                    currentCoroutineContext().ensureActive()
                    val hash = withContext(Dispatchers.Default) {
                        MessageDigest.getInstance("SHA-256").digest(payload.bytes).joinToString("") { "%02x".format(it) } + key.variant.wire
                    }
                    val bitmap = cache[hash] ?: withContext(Dispatchers.Default) { decode(payload) }
                    currentCoroutineContext().ensureActive()
                    if (generation != currentGeneration || key !in desired()) return@withPermit
                    if (hash !in cache) {
                        while (cache.isNotEmpty() && cacheBytes + bitmap.allocationByteCount > MAX_CACHE_BYTES) {
                            val oldest = cache.entries.iterator()
                            cacheBytes -= oldest.next().value.allocationByteCount
                            oldest.remove()
                        }
                        cache[hash] = bitmap
                        cacheBytes += bitmap.allocationByteCount
                    }
                    mutable.value = mutable.value + (key to ImageLoad(bitmap))
                } }
            } catch (error: Exception) {
                if (error is CancellationException && error !is TimeoutCancellationException) throw error
                currentCoroutineContext().ensureActive()
                if (generation != currentGeneration || key !in desired()) return@launch
                val failure = if (error is TimeoutCancellationException) ApiFailure(504, "preview_timeout")
                    else error as? ApiFailure ?: ApiFailure(0, "preview_invalid")
                if (failure.status == 401 || failure.status == 403) { pause(); rejected(failure) }
                else mutable.value = mutable.value + (key to ImageLoad(error = failure.imageMessage()))
            } finally {
                if (jobs[key] == currentCoroutineContext()[Job]) jobs.remove(key)
            }
        }
        jobs[key] = job
        job.start()
    }

    companion object {
        const val MAX_VISIBLE = 32
        private const val MAX_CACHE_BYTES = 24 * 1024 * 1024

        internal fun decode(payload: ImagePayload): Bitmap {
            val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
            BitmapFactory.decodeByteArray(payload.bytes, 0, payload.bytes.size, bounds)
            if (bounds.outWidth != payload.width || bounds.outHeight != payload.height || bounds.outMimeType != "image/png")
                throw ApiFailure(0, "preview_invalid")
            val options = BitmapFactory.Options().apply {
                inSampleSize = if (payload.variant == ImageVariant.THUMBNAIL && maxOf(payload.width, payload.height) > 256) 2 else 1
                inPreferredConfig = Bitmap.Config.ARGB_8888
                inScaled = false
            }
            val bitmap = BitmapFactory.decodeByteArray(payload.bytes, 0, payload.bytes.size, options)
                ?: throw ApiFailure(0, "preview_invalid")
            if (bitmap.width > payload.variant.maxEdge || bitmap.height > payload.variant.maxEdge ||
                bitmap.allocationByteCount > payload.variant.maxEdge * payload.variant.maxEdge * 4) {
                throw ApiFailure(0, "preview_limit_exceeded")
            }
            return bitmap
        }
    }
}
