package net.anibel.app

import androidx.activity.compose.setContent
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithTag
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTouchInput
import androidx.compose.ui.test.click
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertTextContains
import androidx.test.ext.junit.runners.AndroidJUnit4
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class LiveReaderTest {
    @get:Rule val compose = createAndroidComposeRule<MobileActivity>()

    @Test fun liveChapterDisplaysDecodedPage(): Unit = runBlocking {
        val core = (compose.activity.application as AnibelApplication).core
        val titles = core.call("mediaList", json("mediaType" to "manga", "limit" to 10)).getJSONArray("docs").objects()
        val title = titles.first { core.call("chapters", json("mediaId" to it.getString("mediaId"), "limit" to 1000)).getJSONArray("docs").length() > 0 }
        val chapters = core.call("chapters", json("mediaId" to title.getString("mediaId"), "limit" to 1000)).getJSONArray("docs").objects()
        val args = json("slug" to title.getString("slug"), "chapter" to chapters.first().getDouble("chapter"),
            "chapters" to org.json.JSONArray(chapters.map { it.getDouble("chapter") }))
        println("Reader title: ${title.getString("slug")}")
        compose.activityRule.scenario.onActivity { activity ->
            activity.setContent { AppHost(false) { ReaderScreen(args, false) } }
        }
        compose.waitUntil(60_000) {
            compose.onAllNodesWithTag("reader_page_ready").fetchSemanticsNodes().isNotEmpty() ||
                compose.onAllNodesWithTag("reader_page_error").fetchSemanticsNodes().isNotEmpty()
        }
        assertTrue("A manga page must decode", compose.onAllNodesWithTag("reader_page_ready").fetchSemanticsNodes().isNotEmpty())
        compose.runOnIdle {
            assertTrue(!androidx.core.view.ViewCompat.getRootWindowInsets(compose.activity.window.decorView)!!
                .isVisible(androidx.core.view.WindowInsetsCompat.Type.systemBars()))
        }
        compose.onNodeWithTag("reader_fullscreen").performTouchInput { click(center) }
        compose.onNodeWithTag("reader_settings").performClick()
        compose.onNodeWithText(ui(R.string.read_right_to_left)).performClick()
        compose.onNodeWithTag("reader_pager").assertIsDisplayed()
        compose.onNodeWithTag("reader_progress").assertIsDisplayed()
        compose.onNodeWithTag("reader_settings").performClick()
        compose.onNodeWithText(ui(R.string.scroll_pages)).performClick()
        compose.onNodeWithTag("reader_pages").assertIsDisplayed()
        androidx.test.platform.app.InstrumentationRegistry.getInstrumentation().uiAutomation.takeScreenshot()?.let { bitmap ->
            java.io.File(compose.activity.getExternalFilesDir(null), "reader-controls-test.png").outputStream().use {
                bitmap.compress(android.graphics.Bitmap.CompressFormat.PNG, 100, it)
            }
            bitmap.recycle()
        }
    }
}
