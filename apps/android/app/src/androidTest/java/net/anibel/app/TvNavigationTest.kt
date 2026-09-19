package net.anibel.app

import androidx.compose.ui.input.key.Key
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsFocused
import androidx.compose.ui.test.assertIsSelected
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.performScrollToIndex
import androidx.compose.ui.test.performSemanticsAction
import androidx.compose.ui.semantics.SemanticsActions
import androidx.activity.compose.setContent
import androidx.lifecycle.ViewModelProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class TvNavigationTest {
    @get:Rule val compose = createAndroidComposeRule<TvActivity>()

    private fun press(key: Key) {
        compose.waitForIdle()
        val code = when (key) {
            Key.DirectionDown -> android.view.KeyEvent.KEYCODE_DPAD_DOWN
            Key.DirectionRight -> android.view.KeyEvent.KEYCODE_DPAD_RIGHT
            Key.DirectionLeft -> android.view.KeyEvent.KEYCODE_DPAD_LEFT
            Key.DirectionCenter -> android.view.KeyEvent.KEYCODE_DPAD_CENTER
            else -> error("Unsupported test key")
        }
        androidx.test.platform.app.InstrumentationRegistry.getInstrumentation().sendKeyDownUpSync(code)
        compose.waitForIdle()
    }

    @Test
    fun libraryTabsSwitchOnRemoteFocus() {
        repeat(6) { press(Key.DirectionDown) }
        compose.onNodeWithTag("nav_Favorites").assertIsSelected()
        press(Key.DirectionRight)
        compose.onNodeWithTag("saved_Favorites").assertIsFocused()
        press(Key.DirectionRight)
        compose.onNodeWithTag("saved_InProgress").assertIsFocused().assertIsSelected()
        compose.onNodeWithTag("saved_catalog_Manga").performSemanticsAction(SemanticsActions.RequestFocus) { it() }
        compose.onNodeWithTag("saved_catalog_Manga").assertIsSelected()
        compose.activityRule.scenario.onActivity { it.onBackPressedDispatcher.onBackPressed() }
        compose.onNodeWithTag("nav_Favorites").assertIsFocused()
    }

    @Test
    fun personalDialogsWorkWithRemote() {
        var chosen = 0
        compose.activityRule.scenario.onActivity { activity -> activity.setContent {
            SystemTheme(true) {
                TvPersonalTitleActions(false, "watching", 3.0, listOf("notselected", "watching", "watched"), false,
                    {}, {}, { chosen = it })
            }
        } }
        compose.onNodeWithTag("detail_rating").performSemanticsAction(SemanticsActions.RequestFocus) { it() }
        press(Key.DirectionCenter)
        compose.onNodeWithTag("rate_1").assertIsFocused()
        repeat(3) { press(Key.DirectionDown) }
        compose.onNodeWithTag("rate_4").assertIsFocused()
        press(Key.DirectionCenter)
        compose.runOnIdle { org.junit.Assert.assertEquals(4, chosen) }
    }

    @Test
    fun remoteCanReachContentReturnToSidebarAndOpenFooterPages() {
        compose.onNodeWithTag("nav_Home").assertIsFocused()
        listOf("Anime", "Manga", "Cinema", "Games", "Books", "Favorites", "Profile", "Download", "Settings").forEach { destination ->
            press(Key.DirectionDown)
            compose.onNodeWithTag("nav_$destination").assertIsFocused()
            compose.onNodeWithTag("page_$destination").assertIsDisplayed()
            compose.onNodeWithTag("nav_$destination").assertIsSelected()
            if (destination == "Anime") {
                press(Key.DirectionRight)
                compose.onNodeWithTag("filters").assertIsFocused()
                compose.activityRule.scenario.onActivity { it.onBackPressedDispatcher.onBackPressed() }
                compose.onNodeWithTag("nav_Anime").assertIsFocused()
            }
        }
        compose.activityRule.scenario.recreate()
        compose.onNodeWithTag("page_Settings").assertIsDisplayed()
        compose.onNodeWithTag("nav_Settings").assertIsFocused()
        compose.activityRule.scenario.onActivity { it.onBackPressedDispatcher.onBackPressed() }
        compose.onNodeWithTag("page_Home").assertIsDisplayed()
        compose.onNodeWithTag("nav_Home").assertIsFocused()
    }

    @Test
    fun catalogLoadsNextPageAndFiltersWorkWithRemote() {
        press(Key.DirectionDown)
        val model = ViewModelProvider(compose.activity)[CatalogModel::class.java]
        compose.waitUntil(30_000) { model.state == LoadState.Ready && model.titles.isNotEmpty() }
        val firstPageSize = model.titles.size
        check(model.hasMore)
        compose.onNodeWithTag("titles").performScrollToIndex(firstPageSize - 1)
        compose.waitUntil(30_000) { model.titles.size > firstPageSize }
        press(Key.DirectionRight)
        compose.onNodeWithTag("filters").assertIsFocused()
        press(Key.DirectionCenter)
        compose.waitUntil(30_000) { model.filterState == LoadState.Ready }
        compose.onNodeWithTag("filter_category_Type").assertIsFocused()
        press(Key.DirectionRight)
        press(Key.DirectionCenter)
        compose.waitUntil(10_000) { model.selected[FilterKey.Type].orEmpty().isNotEmpty() }
        press(Key.DirectionLeft)
        compose.onNodeWithTag("filter_category_Type").assertIsFocused()
        press(Key.DirectionDown)
        compose.onNodeWithTag("filter_category_Year").assertIsFocused()
        press(Key.DirectionRight)
        press(Key.DirectionCenter)
        compose.waitUntil(10_000) { model.selected[FilterKey.Year].orEmpty().isNotEmpty() }
        androidx.test.platform.app.InstrumentationRegistry.getInstrumentation().sendKeyDownUpSync(android.view.KeyEvent.KEYCODE_BACK)
        compose.onNodeWithTag("page_Anime").assertIsDisplayed()
    }
}
