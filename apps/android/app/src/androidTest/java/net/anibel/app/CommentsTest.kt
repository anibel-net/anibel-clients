package net.anibel.app

import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.*
import androidx.compose.material3.Surface
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.Modifier
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.unit.dp
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.junit.Assert.assertEquals

@RunWith(AndroidJUnit4::class)
class CommentsTest {
    @get:Rule val compose = createAndroidComposeRule<MobileActivity>()
    @Test fun repliesExpandAndReplySelectsTheCorrectComment() {
        var selected = ""
        val comment = json("id" to "parent", "content" to "The animation in this episode is beautiful. Looking forward to the next one!",
            "created" to 1789804800000L, "user" to json("username" to "reader", "displayName" to "Reader"),
            "replies" to org.json.JSONArray().put(json("id" to "child", "content" to "The background art is great too.",
                "created" to 1789808400000L, "user" to json("username" to "viewer", "displayName" to "Viewer"))))
        compose.activityRule.scenario.onActivity { activity -> activity.setContent {
            SystemTheme { Surface(Modifier.fillMaxSize()) {
                CompositionLocalProvider(LocalSession provides json("authenticated" to true)) {
                    Column(Modifier.statusBarsPadding().padding(24.dp)) {
                        CommentItem(comment, false, 0) { selected = it.getString("id") }
                    }
                }
            } }
        } }
        compose.onNodeWithText("Viewer").assertDoesNotExist()
        compose.onNodeWithText(ui(R.string.show_replies, 1)).performClick()
        compose.onNodeWithText("Viewer").assertIsDisplayed()
        compose.onAllNodesWithText(ui(R.string.reply))[1].performClick()
        compose.runOnIdle { assertEquals("child", selected) }
        androidx.test.platform.app.InstrumentationRegistry.getInstrumentation().uiAutomation.takeScreenshot()?.let { bitmap ->
            java.io.File(compose.activity.getExternalFilesDir(null), "comments-test.png").outputStream().use {
                bitmap.compress(android.graphics.Bitmap.CompressFormat.PNG, 100, it)
            }
            bitmap.recycle()
        }
        compose.onNodeWithText(ui(R.string.hide_replies, 1)).performClick()
        compose.onNodeWithText("Viewer").assertDoesNotExist()
    }
}
