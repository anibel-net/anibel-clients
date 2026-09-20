package net.anibel.app

import androidx.activity.compose.setContent
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithTag
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.assertIsDisplayed
import androidx.test.platform.app.InstrumentationRegistry
import kotlinx.coroutines.runBlocking
import org.junit.Rule
import org.junit.Test
import org.junit.Assert.*
import java.io.File
import java.security.MessageDigest

/** Real decoder/frame and close checks using generated media and isolated core storage. */
class LocalPlaybackTest {
    @get:Rule val compose = createAndroidComposeRule<MobileActivity>()
    @Test fun localVideoRendersSeeksAndCloses() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val folder = File(compose.activity.cacheDir, "local-playback-${java.util.UUID.randomUUID()}").apply { mkdirs() }
        val id = "fixture"
        val hash = MessageDigest.getInstance("SHA-256").digest(id.toByteArray()).joinToString("") { "%02x".format(it) }
        val relative = "downloads/$hash/video.mp4"
        File(folder, relative).apply {
            parentFile!!.mkdirs()
            instrumentation.context.assets.open("video.mp4").use { input -> outputStream().use { input.copyTo(it) } }
        }
        File(folder, "downloads.json").writeText(org.json.JSONArray().put(json("id" to id, "kind" to "video",
            "request" to json("episodeId" to "fixture-episode", "episodeUrl" to "https://example.invalid/video", "title" to "Fixture"),
            "status" to "completed", "assets" to json("videoPath" to relative), "bytes" to 0, "parts_done" to 1,
            "parts_total" to 1, "created" to 0)).toString())
        val core = CoreClient(folder)
        try {
            compose.activityRule.scenario.onActivity { activity ->
                activity.setContent {
                    CompositionLocalProvider(LocalCore provides core) {
                        PlayerScreen(json("downloadId" to id), activity.packageManager.hasSystemFeature("android.software.leanback"))
                    }
                }
            }
            compose.waitUntil(30_000) { compose.onAllNodesWithTag("player_ready").fetchSemanticsNodes().isNotEmpty() || compose.onAllNodesWithTag("player_error").fetchSemanticsNodes().isNotEmpty() }
            compose.onNodeWithTag("player_ready").assertIsDisplayed()
            lateinit var player: androidx.media3.common.Player
            compose.runOnIdle {
                player = compose.activity.findViewById<androidx.media3.ui.PlayerView>(R.id.native_player).player!!
                assertTrue(player.duration > 0)
                assertTrue(player.currentTracks.groups.any { it.type == androidx.media3.common.C.TRACK_TYPE_AUDIO })
                player.pause(); player.seekTo(2000)
            }
            compose.waitUntil(10_000) {
                var ready = false
                instrumentation.runOnMainSync { ready = player.playbackState == androidx.media3.common.Player.STATE_READY && kotlin.math.abs(player.currentPosition - 2000) < 500 }
                ready
            }
            compose.activityRule.scenario.onActivity { it.setContent {} }
            compose.waitForIdle()
        } finally {
            compose.activityRule.scenario.onActivity { it.setContent {} }
            compose.waitForIdle()
            runBlocking { core.close() }
            folder.deleteRecursively()
        }
    }
}
