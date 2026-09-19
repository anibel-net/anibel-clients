package net.anibel.app

import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.*
import androidx.compose.material3.Surface
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
class PersonalTitleActionsTest {
    @get:Rule val compose = createAndroidComposeRule<MobileActivity>()
    @Test fun progressAndRatingPickersUseTheSelectedValues() {
        var status = ""
        var rating = 0
        var favorites = 0
        compose.activityRule.scenario.onActivity { activity -> activity.setContent {
            SystemTheme { Surface(Modifier.fillMaxSize()) {
                Column(Modifier.statusBarsPadding().padding(24.dp)) {
                    PersonalTitleActions(json("mark" to json("status" to "watching"), "iRated" to 6),
                        json("marks" to org.json.JSONArray(listOf("notselected", "watching", "watched", "planned", "dropped"))),
                        false, { favorites++ }, { status = it }, { rating = it })
                }
            } }
        } }
        compose.onNodeWithTag("detail_progress").performClick()
        compose.onNodeWithText(ui(R.string.choice_watched)).performClick()
        compose.runOnIdle { assertEquals("watched", status) }
        compose.onNodeWithTag("detail_rating").performClick()
        compose.onNodeWithTag("rate_4").performClick()
        compose.runOnIdle { assertEquals(4, rating) }
        compose.onNodeWithTag("detail_favorite").performClick()
        compose.runOnIdle { assertEquals(1, favorites) }
    }
}
