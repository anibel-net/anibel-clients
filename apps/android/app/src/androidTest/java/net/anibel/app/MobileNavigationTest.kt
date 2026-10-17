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
import androidx.compose.ui.test.hasAnyAncestor
import androidx.compose.ui.test.hasTestTag
import androidx.compose.ui.test.hasText
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.getOrNull
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class MobileNavigationTest {
    @get:Rule val compose = createAndroidComposeRule<MobileActivity>()

    private fun isTablet() = compose.onAllNodes(hasTestTag("tablet_navigation")).fetchSemanticsNodes().isNotEmpty()

    private fun selectPage(page: AppPage) {
        val item = compose.onNodeWithTag("nav_${page.name}")
        if (isTablet() && page !in listOf(AppPage.Profile, AppPage.Favorites, AppPage.Download, AppPage.Settings)) item.performScrollTo()
        item.performClick()
    }

    private fun selectCatalog(catalog: AppPage) {
        if (isTablet()) selectPage(catalog)
        else {
            compose.onNodeWithTag("nav_Catalogs").performClick()
            compose.onNodeWithTag("catalog_${catalog.name}").performClick()
        }
    }

    private fun assertCatalogSelected(catalog: AppPage) {
        if (isTablet()) compose.onNodeWithTag("nav_${catalog.name}").assertIsSelected()
        else {
            compose.onNodeWithTag("nav_Catalogs").assertIsSelected()
            compose.onNodeWithTag("catalog_${catalog.name}").assertIsSelected()
        }
    }

    private fun openProfileLink(label: String) {
        compose.onNode(hasText(label) and hasAnyAncestor(hasTestTag("page_Profile"))).performScrollTo().performClick()
    }

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
        selectPage(AppPage.Favorites)
        compose.onNodeWithTag("saved_catalog_All").assertIsSelected()
        val mediaTab = compose.onNodeWithTag("saved_catalog_All").fetchSemanticsNode().boundsInRoot
        val statusTab = compose.onNodeWithTag("saved_Favorites").fetchSemanticsNode().boundsInRoot
        org.junit.Assert.assertTrue(mediaTab.bottom <= statusTab.top)
        if (isTablet()) {
            val content = compose.onNodeWithTag("page_Favorites").fetchSemanticsNode().boundsInRoot
            org.junit.Assert.assertTrue(statusTab.bottom <= content.top)
        }
        compose.onNodeWithTag("saved_Done").performClick().assertIsSelected()
        compose.onNodeWithTag("saved_catalog_Manga").performClick().assertIsSelected()
        compose.activityRule.scenario.recreate()
        compose.onNodeWithTag("saved_Done").assertIsSelected()
        compose.onNodeWithTag("saved_catalog_Manga").assertIsSelected()
        compose.onNodeWithTag("nav_Profile").performClick()
        selectPage(AppPage.Favorites)
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
        selectCatalog(AppPage.Anime)
        compose.onNodeWithTag("filters").performClick()
        compose.waitUntil(45_000) { compose.onAllNodes(androidx.compose.ui.test.hasText(ui(R.string.filter_year))).fetchSemanticsNodes().isNotEmpty() }
        compose.onNodeWithText(ui(R.string.filter_year)).performClick()
        compose.onNodeWithText("2025").performScrollTo().performClick()
        compose.onNodeWithText(ui(R.string.done), substring = true).performClick()
        compose.onNodeWithContentDescription(ui(R.string.filters_count, 1)).assertIsDisplayed()
    }

    @Test
    fun searchSubmitsFromKeyboard() {
        if (isTablet()) selectPage(AppPage.Search)
        else compose.onNodeWithTag("open_search").performClick()
        compose.onNodeWithText(ui(R.string.search_titles)).performTextInput("Avatar")
        compose.onNode(androidx.compose.ui.test.hasSetTextAction()).performImeAction()
        val titles = SemanticsMatcher("title card") { it.config.getOrNull(SemanticsProperties.TestTag)?.startsWith("title_") == true }
        compose.waitUntil(45_000) { compose.onAllNodes(titles).fetchSemanticsNodes().isNotEmpty() }
    }

    @Test
    fun languageSwitchPersistsAndReturnsToBelarusian() {
        compose.onNodeWithTag("nav_Profile").performClick()
        openProfileLink(ui(R.string.settings))
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
        selectCatalog(AppPage.Anime)
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
        assertCatalogSelected(AppPage.Anime)
    }

    @Test
    fun profileLinksOpenNativePagesAndBackKeepsProfile() {
        compose.onNodeWithTag("nav_Profile").performClick()
        // The device can have an existing signed-in session.
        openProfileLink(ui(R.string.nav_settings))
        compose.onNodeWithTag("screen_Settings").assertIsDisplayed()
        compose.onNodeWithTag("back").performClick()
        compose.onNodeWithTag("nav_Profile").assertIsSelected()
        openProfileLink(ui(R.string.nav_download))
        compose.onNodeWithTag("screen_Downloads").assertIsDisplayed()
        compose.onNodeWithTag("back").performClick()
        compose.onNodeWithTag("nav_Profile").assertIsSelected()
    }

    @Test
    fun tabsAndCatalogSwitcherReturnHome() {
        listOf("Favorites", "Profile", "Home").forEach { destination ->
            selectPage(AppPage.valueOf(destination))
            compose.onNodeWithTag("nav_$destination").assertIsSelected()
            compose.onNodeWithTag("page_$destination").assertIsDisplayed()
        }
        selectCatalog(AppPage.Anime)
        compose.onNodeWithTag("page_Catalogs").assertIsDisplayed()
        compose.onNodeWithTag("page_Anime").assertIsDisplayed()
        assertCatalogSelected(AppPage.Anime)

        compose.activityRule.scenario.onActivity { it.onBackPressedDispatcher.onBackPressed() }
        compose.onNodeWithTag("page_Home").assertIsDisplayed()
        compose.onNodeWithTag("nav_Home").assertIsSelected()
    }

    @Test
    fun selectedCatalogSurvivesActivityRecreation() {
        selectCatalog(AppPage.Books)
        compose.activityRule.scenario.recreate()
        compose.onNodeWithTag("page_Books").assertIsDisplayed()
        assertCatalogSelected(AppPage.Books)
    }
}
