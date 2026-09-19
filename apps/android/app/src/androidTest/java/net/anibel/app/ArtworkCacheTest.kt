package net.anibel.app

import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import coil3.SingletonImageLoader
import coil3.decode.DataSource
import coil3.request.CachePolicy
import coil3.request.ImageRequest
import coil3.request.SuccessResult
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class ArtworkCacheTest {
    @Test fun screenshotMetadataAndImageAreReused() = runBlocking {
        val app = ApplicationProvider.getApplicationContext<AnibelApplication>()
        val cache = app.episodeArtwork
        val id = "4624f156-b4f3-4b19-9e0c-ec559a038c57"
        val first = cache.request(id, app.core)
        val concurrent = cache.request(id, app.core)
        val images = first.await()
        assertTrue(images.isNotEmpty())
        assertEquals(images, concurrent.await())
        assertEquals(images, cache.peek(id))
        assertEquals(images, cache.request(id, app.core).await())

        val loader = SingletonImageLoader.get(app)
        val request = ImageRequest.Builder(app).data(images.first()).size(320, 180).build()
        assertTrue(loader.execute(request) is SuccessResult)
        assertEquals(DataSource.MEMORY_CACHE, (loader.execute(request) as SuccessResult).dataSource)
        loader.memoryCache!!.clear()
        // No network is allowed: this must come from the persistent image cache.
        val offline = request.newBuilder().networkCachePolicy(CachePolicy.DISABLED).build()
        assertEquals(DataSource.DISK, (loader.execute(offline) as SuccessResult).dataSource)
    }
}
