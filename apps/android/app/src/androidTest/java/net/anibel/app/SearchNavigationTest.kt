package net.anibel.app

import android.view.View
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.core.view.ViewCompat
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.After
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.junit.rules.ExternalResource
import org.junit.runner.RunWith

/** No query is submitted. Search history is local to the app. */
@RunWith(AndroidJUnit4::class)
class SearchNavigationTest {
    @get:Rule(order = 0) val touchMode = object : ExternalResource() {
        override fun before() { InstrumentationRegistry.getInstrumentation().setInTouchMode(true) }
    }
    @get:Rule(order = 1) val compose = createAndroidComposeRule<MobileActivity>()
    private var previousKeyboardSetting: Int? = null

    private fun shell(command: String): String {
        val descriptor = InstrumentationRegistry.getInstrumentation().uiAutomation.executeShellCommand(command)
        return android.os.ParcelFileDescriptor.AutoCloseInputStream(descriptor).bufferedReader().use { it.readText() }
    }

    @Before fun enableSoftwareKeyboard() {
        previousKeyboardSetting = shell("settings get secure show_ime_with_hard_keyboard").trim().toIntOrNull()
        shell("settings put secure show_ime_with_hard_keyboard 1")
    }

    @After fun restoreKeyboardSetting() {
        compose.mainClock.autoAdvance = true
        val value = previousKeyboardSetting
        shell(if (value == null) "settings delete secure show_ime_with_hard_keyboard"
            else "settings put secure show_ime_with_hard_keyboard $value")
    }

    private fun isTablet() = compose.onAllNodesWithTag("tablet_navigation").fetchSemanticsNodes().isNotEmpty()

    private fun openSearch() {
        if (isTablet()) compose.onNodeWithTag("nav_Search").performScrollTo().performClick()
        else compose.onNodeWithTag("open_search").performClick()
    }

    private fun keyboardVisible(): Boolean {
        val view: View = compose.activity.window.decorView
        return ViewCompat.getRootWindowInsets(view)?.isVisible(WindowInsetsCompat.Type.ime()) == true
    }

    @Test fun openingSearchFocusesTheInputAndShowsTheKeyboard() {
        openSearch()
        compose.onNodeWithTag("search_input").assertIsFocused()
        val inputHeight = compose.onNodeWithTag("search_input").fetchSemanticsNode().boundsInRoot.height
        org.junit.Assert.assertTrue("The input must leave room for results",
            inputHeight <= 80f * compose.activity.resources.displayMetrics.density)
        compose.waitUntil(10_000) { keyboardVisible() }
        compose.onNodeWithTag("search_input").performTextInput("test")
        compose.onNodeWithTag("search_input").assertTextContains("test")
        compose.runOnIdle {
            WindowCompat.getInsetsController(compose.activity.window, compose.activity.window.decorView)
                .hide(WindowInsetsCompat.Type.ime())
        }
        compose.waitUntil(10_000) { !keyboardVisible() }
        compose.onNodeWithTag("search_input").performClick()
        compose.waitUntil(10_000) { keyboardVisible() }
    }

    @Test fun leavingSearchNeverShowsTwoInputs() {
        openSearch()
        compose.onNodeWithTag("search_input").assertIsFocused()
        val tablet = isTablet()
        compose.mainClock.autoAdvance = false
        if (tablet) compose.onNodeWithTag("nav_Anime").performScrollTo().performClick()
        else compose.onNodeWithTag("back").performClick()
        repeat(24) {
            compose.mainClock.advanceTimeByFrame()
            compose.onAllNodes(hasTestTag("search_input") or hasTestTag("open_search")).assertCountEquals(1)
        }
        compose.mainClock.autoAdvance = true
        compose.waitUntil(10_000) { !keyboardVisible() }
        if (!tablet) compose.onNodeWithTag("nav_Catalogs").performClick()
        compose.onNodeWithTag("page_Anime").assertIsDisplayed()
        compose.onNodeWithTag("search_input").assertDoesNotExist()
        compose.onNodeWithTag("open_search").assertIsNotFocused()
    }
}
