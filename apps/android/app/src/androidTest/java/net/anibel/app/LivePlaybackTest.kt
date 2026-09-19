package net.anibel.app

import androidx.activity.compose.setContent
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithTag
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.performSemanticsAction
import androidx.compose.ui.test.performTouchInput
import androidx.compose.ui.test.click
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performClick
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class LivePlaybackTest {
    @get:Rule val compose = createAndroidComposeRule<MobileActivity>()

    @Test fun sharedCoreSourcePlaysInNativePlayer() {
        compose.activityRule.scenario.onActivity { activity ->
            activity.setContent {
                AppHost(false) {
                    // No episode ID: this smoke check does not write history or resume data.
                    val navigator = LocalNavigator.current
                    val opened = androidx.compose.runtime.saveable.rememberSaveable { androidx.compose.runtime.mutableStateOf(false) }
                    androidx.compose.material3.Text("Playback test", Modifier.testTag("playback_origin"))
                    androidx.compose.runtime.LaunchedEffect(Unit) {
                        if (!opened.value) {
                            opened.value = true
                            navigator.open(Screen.Player, json("url" to "https://video.anibel.net/4624f156-b4f3-4b19-9e0c-ec559a038c57?type=anime", "episodeType" to "sub"))
                        }
                    }
                }
            }
        }
        compose.waitUntil(90_000) {
            compose.onAllNodesWithTag("player_ready").fetchSemanticsNodes().isNotEmpty() ||
                compose.onAllNodesWithTag("player_error").fetchSemanticsNodes().isNotEmpty()
        }
        compose.onAllNodesWithTag("player_error").fetchSemanticsNodes().forEach { println(it.config) }
        compose.onNodeWithTag("player_ready").assertIsDisplayed()
        compose.waitUntil(10_000) { compose.activity.resources.configuration.orientation == android.content.res.Configuration.ORIENTATION_LANDSCAPE }
        compose.runOnIdle {
            org.junit.Assert.assertEquals(android.content.res.Configuration.ORIENTATION_LANDSCAPE, compose.activity.resources.configuration.orientation)
            val bars = androidx.core.view.ViewCompat.getRootWindowInsets(compose.activity.window.decorView)
            org.junit.Assert.assertFalse(bars!!.isVisible(androidx.core.view.WindowInsetsCompat.Type.systemBars()))
        }
        androidx.test.platform.app.InstrumentationRegistry.getInstrumentation().uiAutomation
            .takeScreenshot()?.let { bitmap ->
                java.io.File(compose.activity.getExternalFilesDir(null), "player-test.png").outputStream().use {
                    bitmap.compress(android.graphics.Bitmap.CompressFormat.PNG, 100, it)
                }
                bitmap.recycle()
            }
        compose.runOnIdle { compose.activity.findViewById<androidx.media3.ui.PlayerView>(R.id.native_player).apply { controllerShowTimeoutMs = 0; showController() } }
        compose.onNodeWithTag("player_back").assertIsDisplayed()
        compose.onNodeWithTag("player_play_pause").performClick()
        compose.runOnIdle { org.junit.Assert.assertFalse(compose.activity.findViewById<androidx.media3.ui.PlayerView>(R.id.native_player).player!!.playWhenReady) }
        compose.onNodeWithTag("player_fill").performClick()
        compose.runOnIdle {
            org.junit.Assert.assertEquals(androidx.media3.ui.AspectRatioFrameLayout.RESIZE_MODE_ZOOM,
                compose.activity.findViewById<androidx.media3.ui.PlayerView>(R.id.native_player).resizeMode)
        }
        compose.onNodeWithTag("player_fill").performClick()
        compose.runOnIdle {
            org.junit.Assert.assertEquals(androidx.media3.ui.AspectRatioFrameLayout.RESIZE_MODE_FIT,
                compose.activity.findViewById<androidx.media3.ui.PlayerView>(R.id.native_player).resizeMode)
        }
        pinchVideo(expand = true)
        compose.runOnIdle {
            org.junit.Assert.assertEquals(androidx.media3.ui.AspectRatioFrameLayout.RESIZE_MODE_ZOOM,
                compose.activity.findViewById<androidx.media3.ui.PlayerView>(R.id.native_player).resizeMode)
        }
        pinchVideo(expand = false)
        compose.runOnIdle {
            org.junit.Assert.assertEquals(androidx.media3.ui.AspectRatioFrameLayout.RESIZE_MODE_FIT,
                compose.activity.findViewById<androidx.media3.ui.PlayerView>(R.id.native_player).resizeMode)
        }
        var beforeSeek = 0L
        compose.runOnIdle { beforeSeek = compose.activity.findViewById<androidx.media3.ui.PlayerView>(R.id.native_player).player!!.currentPosition }
        compose.onNodeWithTag("player_seek_forward").performClick()
        compose.runOnIdle { org.junit.Assert.assertTrue(compose.activity.findViewById<androidx.media3.ui.PlayerView>(R.id.native_player).player!!.currentPosition > beforeSeek) }
        compose.onNodeWithTag("player_seek").performSemanticsAction(SemanticsActions.SetProgress) { it(0.25f) }
        compose.runOnIdle {
            val engine = compose.activity.findViewById<androidx.media3.ui.PlayerView>(R.id.native_player).player!!
            org.junit.Assert.assertTrue(kotlin.math.abs(engine.currentPosition - engine.duration / 4) < 2000)
        }
        compose.onNodeWithTag("player_subtitles").performClick()
        compose.onNodeWithText(ui(R.string.player_off)).performClick()
        compose.runOnIdle { org.junit.Assert.assertTrue(androidx.media3.common.C.TRACK_TYPE_TEXT in
            compose.activity.findViewById<androidx.media3.ui.PlayerView>(R.id.native_player).player!!.trackSelectionParameters.disabledTrackTypes) }
        compose.onNodeWithTag("player_settings").performClick()
        compose.onNodeWithText(ui(R.string.player_speed)).performClick()
        compose.onNodeWithText("1.5×").performScrollTo().performClick()
        compose.runOnIdle { org.junit.Assert.assertEquals(1.5f,
            compose.activity.findViewById<androidx.media3.ui.PlayerView>(R.id.native_player).player!!.playbackParameters.speed) }
        compose.waitForIdle()
        compose.onNodeWithTag("player_back").assertIsDisplayed()
        // Wait for the Android dialog window exit animation before the visual capture.
        android.os.SystemClock.sleep(400)
        androidx.test.platform.app.InstrumentationRegistry.getInstrumentation().uiAutomation.takeScreenshot()?.let { bitmap ->
            java.io.File(compose.activity.getExternalFilesDir(null), "material-player-test.png").outputStream().use {
                bitmap.compress(android.graphics.Bitmap.CompressFormat.PNG, 100, it)
            }
            bitmap.recycle()
        }
        compose.onNodeWithTag("player_play_pause").performClick()
        compose.waitUntil(8_000) {
            var hidden = false
            compose.activityRule.scenario.onActivity { hidden = !it.findViewById<androidx.media3.ui.PlayerView>(R.id.native_player).isControllerFullyVisible }
            hidden
        }
        compose.onNodeWithTag("player_ready").performTouchInput { click(center) }
        compose.onNodeWithTag("player_pip").performClick()
        compose.waitUntil(10_000) {
            var ready = false
            compose.activityRule.scenario.onActivity {
                ready = it.isInPictureInPictureMode && !it.findViewById<androidx.media3.ui.PlayerView>(R.id.native_player).useController
            }
            ready
        }
        var pipPosition = 0L
        compose.activityRule.scenario.onActivity {
            val view = it.findViewById<androidx.media3.ui.PlayerView>(R.id.native_player)
            org.junit.Assert.assertFalse(view.useController)
            pipPosition = view.player!!.currentPosition
        }
        compose.waitUntil(10_000) {
            var advancing = false
            compose.activityRule.scenario.onActivity {
                advancing = it.findViewById<androidx.media3.ui.PlayerView>(R.id.native_player).player!!.currentPosition > pipPosition + 500
            }
            advancing
        }
        androidx.test.platform.app.InstrumentationRegistry.getInstrumentation().uiAutomation.takeScreenshot()?.let { bitmap ->
            java.io.File(compose.activity.getExternalFilesDir(null), "pip-player-test.png").outputStream().use {
                bitmap.compress(android.graphics.Bitmap.CompressFormat.PNG, 100, it)
            }
            bitmap.recycle()
        }
        androidx.test.platform.app.InstrumentationRegistry.getInstrumentation().uiAutomation
            .executeShellCommand("am start -n net.anibel.app/.MobileActivity --activity-reorder-to-front").close()
        compose.waitUntil(10_000) {
            var restored = false
            compose.activityRule.scenario.onActivity {
                restored = !it.isInPictureInPictureMode && it.findViewById<androidx.media3.ui.PlayerView>(R.id.native_player).useController
            }
            restored
        }
        android.os.SystemClock.sleep(700)
        // Home should use the same PiP path while playback is active.
        androidx.test.platform.app.InstrumentationRegistry.getInstrumentation().uiAutomation
            .executeShellCommand("input keyevent KEYCODE_HOME").close()
        compose.waitUntil(10_000) {
            var ready = false
            compose.activityRule.scenario.onActivity {
                ready = it.isInPictureInPictureMode && !it.findViewById<androidx.media3.ui.PlayerView>(R.id.native_player).useController
            }
            ready
        }
        // Android's PiP transition runs outside the Compose animation clock.
        android.os.SystemClock.sleep(700)
        androidx.test.platform.app.InstrumentationRegistry.getInstrumentation().uiAutomation
            .executeShellCommand("am start -n net.anibel.app/.MobileActivity --activity-reorder-to-front").close()
        compose.waitUntil(10_000) {
            var restored = false
            compose.activityRule.scenario.onActivity {
                restored = !it.isInPictureInPictureMode && it.findViewById<androidx.media3.ui.PlayerView>(R.id.native_player).useController
            }
            restored
        }
        compose.runOnIdle { compose.activity.findViewById<androidx.media3.ui.PlayerView>(R.id.native_player).showController() }
        compose.onNodeWithTag("player_back").performClick()
        compose.onNodeWithTag("playback_origin").assertIsDisplayed()
        compose.runOnIdle {
            org.junit.Assert.assertEquals(android.content.pm.ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED, compose.activity.requestedOrientation)
        }
    }
    private fun pinchVideo(expand: Boolean) {
        compose.runOnIdle {
            val view = compose.activity.findViewById<androidx.media3.ui.PlayerView>(R.id.native_player)
            val start = android.os.SystemClock.uptimeMillis()
            val points = Array(2) { index -> android.view.MotionEvent.PointerProperties().apply {
                id = index
                toolType = android.view.MotionEvent.TOOL_TYPE_FINGER
            } }
            fun send(action: Int, count: Int, span: Float, time: Long) {
                val coords = Array(count) { index -> android.view.MotionEvent.PointerCoords().apply {
                    x = view.width / 2f + (if (index == 0) -span else span)
                    y = view.height / 2f
                    pressure = 1f
                    size = 1f
                } }
                val event = android.view.MotionEvent.obtain(start, time, action, count, points, coords,
                    0, 0, 1f, 1f, 0, 0, android.view.InputDevice.SOURCE_TOUCHSCREEN, 0)
                view.dispatchTouchEvent(event)
                event.recycle()
            }
            val small = view.width * 0.06f
            val large = view.width * 0.25f
            val from = if (expand) small else large
            val to = if (expand) large else small
            send(android.view.MotionEvent.ACTION_DOWN, 1, from, start)
            send(android.view.MotionEvent.ACTION_POINTER_DOWN or (1 shl 8), 2, from, start + 16)
            for (step in 1..20) send(android.view.MotionEvent.ACTION_MOVE, 2, from + (to - from) * step / 20, start + (step + 1) * 16)
            send(android.view.MotionEvent.ACTION_POINTER_UP or (1 shl 8), 2, to, start + 352)
            send(android.view.MotionEvent.ACTION_UP, 1, to, start + 368)
        }
    }

}
