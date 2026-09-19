package net.anibel.app

import androidx.compose.ui.test.performScrollToIndex
import androidx.compose.ui.test.performTouchInput
import androidx.compose.ui.test.swipeUp
import androidx.compose.ui.test.performImeAction
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.assertTextContains
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsSelected
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.SemanticsMatcher
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.getOrNull
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class MobileNavigationTest {
    @get:Rule val compose = createAndroidComposeRule<MobileActivity>()

    @Test
    fun profileEditingUsesSeparatePageAndBackDiscardsDraft() {
        compose.onNodeWithTag("nav_Profile").performClick()
        compose.waitUntil(15_000) { compose.onAllNodes(androidx.compose.ui.test.hasText(ui(R.string.edit_profile))).fetchSemanticsNodes().isNotEmpty() }
        compose.onNodeWithText(ui(R.string.edit_profile)).performScrollTo().performClick()
        compose.onNodeWithTag("screen_EditProfile").assertIsDisplayed()
        compose.onNodeWithTag("mobile_bottom_bar").assertDoesNotExist()
        compose.onNodeWithText(ui(R.string.display_name)).performTextInput(" Unsaved")
        compose.activityRule.scenario.recreate()
        compose.onNodeWithTag("screen_EditProfile").assertIsDisplayed()
        compose.onNodeWithTag("back").performClick()
        compose.onNodeWithTag("nav_Profile").assertIsSelected()
        compose.onNodeWithText(ui(R.string.display_name)).assertDoesNotExist()
        compose.onNodeWithText(ui(R.string.edit_profile)).performScrollTo().performClick()
        compose.onNodeWithText(" Unsaved", substring = true).assertDoesNotExist()
        compose.onNodeWithTag("back").performClick()
    }

    @Test
    fun savedListAndMediaTabsPersist() {
        compose.onNodeWithTag("nav_Favorites").performClick()
        compose.onNodeWithTag("saved_catalog_All").assertIsSelected()
        val mediaTab = compose.onNodeWithTag("saved_catalog_All").fetchSemanticsNode().boundsInRoot
        val statusTab = compose.onNodeWithTag("saved_Favorites").fetchSemanticsNode().boundsInRoot
        org.junit.Assert.assertTrue(mediaTab.bottom < statusTab.top)
        compose.onNodeWithTag("saved_Done").performClick().assertIsSelected()
        compose.onNodeWithTag("saved_catalog_Manga").performClick().assertIsSelected()
        compose.activityRule.scenario.recreate()
        compose.onNodeWithTag("saved_Done").assertIsSelected()
        compose.onNodeWithTag("saved_catalog_Manga").assertIsSelected()
        compose.onNodeWithTag("nav_Profile").performClick()
        compose.onNodeWithTag("nav_Favorites").performClick()
        compose.onNodeWithTag("saved_Done").assertIsSelected()
        compose.onNodeWithTag("saved_catalog_Manga").assertIsSelected()
    }

    @Test
    fun homeSectionsLoadOlderUpdatesWhenScrolling() {
        compose.waitUntil(45_000) { compose.onAllNodes(androidx.compose.ui.test.hasTestTag("updates_SUB_0")).fetchSemanticsNodes().isNotEmpty() }
        repeat(10) {
            if (compose.onAllNodes(androidx.compose.ui.test.hasTestTag("updates_SUB_6")).fetchSemanticsNodes().isNotEmpty()) return@repeat
            compose.onNodeWithTag("home_updates").performTouchInput { swipeUp(durationMillis = 600) }
        }
        compose.onNodeWithTag("updates_SUB_6").assertExists()
    }

    @Test
    fun homeSectionArrowOpensFullList() {
        compose.waitUntil(45_000) { compose.onAllNodes(androidx.compose.ui.test.hasTestTag("updates_SUB_0")).fetchSemanticsNodes().isNotEmpty() }
        compose.onNode(androidx.compose.ui.test.hasContentDescription(ui(R.string.choice_sub)) and
            androidx.compose.ui.test.hasAnyAncestor(androidx.compose.ui.test.hasTestTag("updates_SUB_0"))).performScrollTo().performClick()
        compose.onNodeWithTag("screen_Collection").assertIsDisplayed()
        compose.onNodeWithTag("back").performClick()
        compose.onNodeWithTag("page_Home").assertIsDisplayed()
    }

    @Test
    fun fullScreenFiltersApplyYear() {
        compose.onNodeWithTag("nav_Catalogs").performClick()
        compose.onNodeWithTag("filters").performClick()
        compose.waitUntil(45_000) { compose.onAllNodes(androidx.compose.ui.test.hasText(ui(R.string.filter_year))).fetchSemanticsNodes().isNotEmpty() }
        compose.onNodeWithText(ui(R.string.filter_year)).performClick()
        compose.onNodeWithText("2025").performScrollTo().performClick()
        compose.onNodeWithText(ui(R.string.done), substring = true).performClick()
        compose.onNodeWithText(ui(R.string.filters_count, 1), useUnmergedTree = true).assertIsDisplayed()
    }

    @Test
    fun searchSubmitsFromKeyboard() {
        compose.onNodeWithTag("open_search").performClick()
        compose.onNodeWithText(ui(R.string.search_titles)).performTextInput("Avatar")
        compose.onNode(androidx.compose.ui.test.hasSetTextAction()).performImeAction()
        val titles = SemanticsMatcher("title card") { it.config.getOrNull(SemanticsProperties.TestTag)?.startsWith("title_") == true }
        compose.waitUntil(45_000) { compose.onAllNodes(titles).fetchSemanticsNodes().isNotEmpty() }
    }

    @Test
    fun languageSwitchPersistsAndReturnsToBelarusian() {
        compose.onNodeWithTag("nav_Profile").performClick()
        compose.onNodeWithText(ui(R.string.settings)).performScrollTo().performClick()
        compose.onNodeWithText("English").performClick()
        compose.waitUntil(10_000) { Language.current(compose.activity) == AppLanguage.English && ui(R.string.settings) == "Settings" }
        compose.onNodeWithText("Settings").assertIsDisplayed()
        compose.activityRule.scenario.recreate()
        compose.onNodeWithText("Settings").assertIsDisplayed()
        compose.onNodeWithText("Беларуская").performClick()
        compose.waitUntil(10_000) { Language.current(compose.activity) == AppLanguage.Belarusian && ui(R.string.settings) == "Налады" }
        compose.onNodeWithText("Налады").assertIsDisplayed()
    }

    @Test
    fun liveTitleOpensDetailsAndBackKeepsCatalog() {
        compose.onNodeWithTag("nav_Catalogs").performClick()
        val titles = SemanticsMatcher("title card") { it.config.getOrNull(SemanticsProperties.TestTag)?.startsWith("title_") == true }
        compose.waitUntil(45_000) { compose.onAllNodes(titles).fetchSemanticsNodes().isNotEmpty() }
        compose.onAllNodes(titles)[0].performClick()
        compose.onNodeWithTag("screen_Detail").assertIsDisplayed()
        compose.onNodeWithTag("title_options").performClick()
        compose.onNodeWithText(ui(R.string.share_title)).assertIsDisplayed()
        compose.onNodeWithText(ui(R.string.copy_link)).assertIsDisplayed()
        compose.onNodeWithText(ui(R.string.open_browser)).assertIsDisplayed()
        androidx.test.espresso.Espresso.pressBack()
        compose.waitUntil(45_000) { compose.onAllNodes(androidx.compose.ui.test.hasText(ui(R.string.content_tab))).fetchSemanticsNodes().isNotEmpty() }
        compose.onNodeWithText(ui(R.string.content_tab)).performScrollTo().assertIsDisplayed()
        compose.onNodeWithText(ui(R.string.comments_tab)).performScrollTo().assertIsDisplayed()
        compose.onNodeWithTag("screen_Detail").performTouchInput { swipeUp() }
        compose.onNodeWithTag("detail_toolbar_title").assertIsDisplayed()
        compose.onNodeWithTag("back").performClick()
        compose.onNodeWithTag("nav_Catalogs").assertIsSelected()
        compose.onNodeWithTag("catalog_Anime").assertIsSelected()
    }

    @Test
    fun profileLinksOpenNativePagesAndBackKeepsProfile() {
        compose.onNodeWithTag("nav_Profile").performClick()
        // The device can have an existing signed-in session.
        compose.onNodeWithText(ui(R.string.nav_settings)).performScrollTo().performClick()
        compose.onNodeWithTag("screen_Settings").assertIsDisplayed()
        compose.onNodeWithTag("back").performClick()
        compose.onNodeWithTag("nav_Profile").assertIsSelected()
        compose.onNodeWithText(ui(R.string.nav_download)).performScrollTo().performClick()
        compose.onNodeWithTag("screen_Downloads").assertIsDisplayed()
        compose.onNodeWithTag("back").performClick()
        compose.onNodeWithTag("nav_Profile").assertIsSelected()
    }

    @Test
    fun tabsAndCatalogSwitcherReturnHome() {
        listOf("Favorites", "Profile", "Home", "Catalogs").forEach { destination ->
            compose.onNodeWithTag("nav_$destination").performClick().assertIsSelected()
            compose.onNodeWithTag("page_$destination").assertIsDisplayed()
        }
        compose.onNodeWithTag("catalog_Anime").performClick()
        compose.onNodeWithTag("page_Anime").assertIsDisplayed()
        compose.onNodeWithTag("nav_Catalogs").assertIsSelected()

        compose.activityRule.scenario.onActivity { it.onBackPressedDispatcher.onBackPressed() }
        compose.onNodeWithTag("page_Home").assertIsDisplayed()
        compose.onNodeWithTag("nav_Home").assertIsSelected()
    }

    @Test
    fun selectedCatalogSurvivesActivityRecreation() {
        compose.onNodeWithTag("nav_Catalogs").performClick()
        compose.onNodeWithTag("catalog_Books").performClick()
        compose.activityRule.scenario.recreate()
        compose.onNodeWithTag("page_Books").assertIsDisplayed()
        compose.onNodeWithTag("nav_Catalogs").assertIsSelected()
    }
}
