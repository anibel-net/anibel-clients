package net.anibel.app

import android.util.LruCache
import kotlinx.coroutines.*

/** Cache only artwork URLs, never playback URLs or track data. */
internal class EpisodeArtworkCache {
    private val results = LruCache<String, List<String>>(256)
    private val pending = mutableMapOf<String, Deferred<List<String>>>()
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    @Synchronized fun peek(id: String): List<String>? = results.get(id)

    @Synchronized fun request(id: String, core: CoreClient): Deferred<List<String>> {
        results.get(id)?.let { return CompletableDeferred(it) }
        return pending.getOrPut(id) {
            scope.async(start = CoroutineStart.LAZY) {
                try {
                    val images = videoScreenshots(core.call("videoInfo", json("videoId" to id)))
                    results.put(id, images)
                    images
                } finally {
                    synchronized(this@EpisodeArtworkCache) { pending.remove(id) }
                }
            }
        }.also { it.start() }
    }
}
