package net.anibel.app

import androidx.test.core.app.ApplicationProvider
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import java.io.File

class LocalCoreLifecycleTest {
    @Test fun androidQueueStartsSuspendedAndClosedCoreRejectsCalls() = runBlocking {
        val app = ApplicationProvider.getApplicationContext<AnibelApplication>()
        val folder = File(app.cacheDir, "core-lifecycle-${java.util.UUID.randomUUID()}")
        val core = CoreClient(folder)
        try {
            val capabilities = core.call(CoreCommand.Capabilities)
            assertEquals("source", capabilities.getJSONArray("downloadFormats").getString(0))
            assertEquals(1, capabilities.getJSONArray("downloadFormats").length())
            val item = core.call(CoreCommand.DownloadEnqueue, json("kind" to "file", "request" to json("mediaId" to "fixture",
                "mediaType" to "books", "title" to "Fixture", "fileUrl" to "https://example.invalid/file.pdf")))
            assertEquals("queued", item.getString("status"))
            core.call(CoreCommand.DownloadsSuspend)
            assertEquals("queued", core.call(CoreCommand.Downloads).getJSONArray("items").getJSONObject(0).getString("status"))
            core.close(); core.close()
            try { core.call(CoreCommand.Session); fail("A closed core accepted a request") } catch (_: IllegalStateException) { }
        } finally { core.close(); folder.deleteRecursively() }
    }
}
