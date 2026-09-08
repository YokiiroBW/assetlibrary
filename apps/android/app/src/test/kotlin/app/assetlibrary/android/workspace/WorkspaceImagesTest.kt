@file:OptIn(kotlinx.coroutines.ExperimentalCoroutinesApi::class)
package app.assetlibrary.android.workspace

import app.assetlibrary.android.protocol.*
import kotlinx.coroutines.awaitCancellation
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.withContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.Assert.*
import org.junit.Test

class WorkspaceImagesTest {
    private fun row(index: Int) = AssetRow(Library("lib", "样例", "online", "read_only", "images"),
        Entry("$index", "lib", "$index.png", "$index.png", "file", "10", "2026-09-08T00:00:00Z"))

    @Test fun `visible requests are capped at two with bounded admission and cancelled on viewport change`() = runTest {
        var active = 0
        var peak = 0
        val requested = mutableListOf<ImageKey>()
        val images = WorkspaceImages(backgroundScope, { key ->
            requested.add(key); active++; peak = maxOf(active, peak)
            try { awaitCancellation() } finally { active-- }
        }, { fail("Unexpected rejection") })
        images.resume(); images.visible((0..99).map(::row)); runCurrent()
        assertEquals(32, images.state.value.size)
        assertEquals(2, active)
        images.visible(listOf(row(90))); runCurrent()
        assertEquals(1, active)
        assertEquals("90", requested.last().entry)
        assertEquals(2, peak)
        images.pause(); runCurrent()
        assertEquals(0, active)
        assertTrue(images.state.value.isEmpty())
        images.visible(listOf(row(1))); runCurrent()
        assertTrue(images.state.value.isEmpty())
    }

    @Test fun `explicit preview cancels covered thumbnails and close reacquires visible thumbnails`() = runTest {
        val requested = mutableListOf<ImageKey>()
        val images = WorkspaceImages(backgroundScope, { key -> requested.add(key); awaitCancellation() }, { fail() })
        images.resume(); images.visible(listOf(row(1), row(2))); runCurrent()
        images.preview(row(3)); runCurrent()
        assertEquals(ImageVariant.PREVIEW, requested.last().variant)
        assertEquals(1, images.state.value.size)
        images.preview(null); runCurrent()
        assertEquals(2, images.state.value.size)
        assertEquals(2, requested.count { it.entry == "1" })
        images.pause()
    }

    @Test fun `authorization failure clears every image and reports rejection once`() = runTest {
        var rejections = 0
        val images = WorkspaceImages(backgroundScope, { throw ApiFailure(403, "denied") }, { rejections++ })
        images.resume(); images.visible(listOf(row(1), row(2))); runCurrent()
        assertTrue(images.state.value.isEmpty())
        assertEquals(1, rejections)
    }
    @Test fun `late denied response from a cancelled viewport request cannot clear its replacement`() = runTest {
        var calls = 0
        var rejections = 0
        val images = WorkspaceImages(backgroundScope, {
            if (++calls == 1) withContext(NonCancellable) { delay(100); throw ApiFailure(403, "late") }
            else awaitCancellation()
        }, { rejections++ })
        images.resume(); images.visible(listOf(row(1))); runCurrent()
        images.visible(emptyList()); images.visible(listOf(row(1))); runCurrent()
        advanceTimeBy(101); runCurrent()
        assertEquals(0, rejections)
        assertEquals(1, images.state.value.size)
        images.pause()
    }
}
