package net.anibel.app

import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class EpisodeArtworkTest {
    @Test fun driveAndOtherHostsDoNotRequestAnibelScreenshots() {
        val id = "4624f156-b4f3-4b19-9e0c-ec559a038c57"
        assertNull(episodeVideoId("https://drive.google.com/file/d/$id/preview"))
        assertNull(episodeVideoId("https://example.com/$id"))
        assertNull(episodeVideoId(null))
        assertEquals(id, episodeVideoId("https://video.anibel.net/$id?type=anime"))
    }

    @Test fun sharedCoreReturnsRealEpisodeScreenshots() = runBlocking {
        val core = ApplicationProvider.getApplicationContext<AnibelApplication>().core
        val info = core.call("videoInfo", json("videoId" to "4624f156-b4f3-4b19-9e0c-ec559a038c57"))
        val screenshots = videoScreenshots(info)
        assertTrue(screenshots.isNotEmpty())
        val request = coil3.request.ImageRequest.Builder(ApplicationProvider.getApplicationContext<AnibelApplication>())
            .data(screenshots.first()).build()
        val result = coil3.SingletonImageLoader.get(ApplicationProvider.getApplicationContext<AnibelApplication>()).execute(request)
        assertTrue(result is coil3.request.SuccessResult)
        val relativeInfo = core.call("videoInfo", json("videoId" to "09af99c7-e69c-4de0-a010-0e6826c5241f"))
        val relativeScreenshots = videoScreenshots(relativeInfo)
        assertTrue(relativeScreenshots.isNotEmpty())
        val relativeRequest = coil3.request.ImageRequest.Builder(ApplicationProvider.getApplicationContext<AnibelApplication>())
            .data(relativeScreenshots.first()).build()
        assertTrue(coil3.SingletonImageLoader.get(ApplicationProvider.getApplicationContext<AnibelApplication>()).execute(relativeRequest) is coil3.request.SuccessResult)
    }
}
