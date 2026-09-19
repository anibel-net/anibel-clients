package net.anibel.app

import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class RefreshTest {
    @get:Rule val compose = createAndroidComposeRule<MobileActivity>()

    @Test fun emptyListCanRefreshAndRefreshCanFinish() {
        var calls = 0
        var refreshing by mutableStateOf(false)
        compose.activityRule.scenario.onActivity { activity ->
            activity.setContent { SystemTheme {
                RefreshablePage(false, refreshing, { calls++; refreshing = true },
                    Modifier.fillMaxSize().testTag("refresh")) { TitleGrid(null, false) }
            } }
        }
        compose.onNodeWithTag("refresh").performTouchInput { swipeDown() }
        compose.runOnIdle { org.junit.Assert.assertEquals(1, calls); refreshing = false }
        compose.waitForIdle()
        compose.onNodeWithTag("refresh").performTouchInput { swipeDown() }
        compose.runOnIdle { org.junit.Assert.assertEquals(2, calls); refreshing = false }
    }
}
