package net.anibel.app

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.animation.*
import androidx.compose.animation.core.tween
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.border
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.foundation.text.input.rememberTextFieldState
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.input.nestedscroll.nestedScroll
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp

class MobileActivity : ComponentActivity() {
    override fun attachBaseContext(base: android.content.Context) { super.attachBaseContext(Appearance.wrap(Language.wrap(base))) }
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent { AppHost(false) { MobileApp() } }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun MobileApp() {
    var page by rememberSaveable { mutableStateOf(AppPage.Home) }
    var catalog by rememberSaveable { mutableStateOf(AppPage.Anime) }
    var savedList by rememberSaveable { mutableStateOf(PersonalList.Favorites) }
    var savedCatalog by rememberSaveable { mutableStateOf<AppPage?>(null) }
    BackHandler(enabled = page != AppPage.Home) { page = AppPage.Home }

    val navigator = LocalNavigator.current
    SystemTheme {
        BoxWithConstraints(Modifier.fillMaxSize()) {
            val wide = maxWidth >= 600.dp
            val searchScroll = key(page) {
                SearchBarDefaults.enterAlwaysSearchBarScrollBehavior(canScroll = { page == AppPage.Home || page == AppPage.Catalogs })
            }
            val compactPage = when (page) {
                AppPage.Search -> AppPage.Home
                AppPage.Download, AppPage.Settings -> AppPage.Profile
                else -> page
            }
            Row(Modifier.fillMaxSize()) {
                if (wide) TabletNavigation(if (page == AppPage.Catalogs) catalog else page) { destination ->
                    if (destination in AppPage.catalogs) {
                        catalog = destination
                        page = AppPage.Catalogs
                    } else page = destination
                }
                Scaffold(Modifier.weight(1f).nestedScroll(searchScroll.nestedScrollConnection),
                    contentWindowInsets = if (page == AppPage.Profile) WindowInsets.safeDrawing.only(WindowInsetsSides.Horizontal) else WindowInsets.safeDrawing,
                    topBar = {
                        Column(if (page == AppPage.Profile) Modifier else Modifier.statusBarsPadding()) {
                            if (page == AppPage.Home || page == AppPage.Catalogs) SearchBar(state = rememberSearchBarState(),
                                modifier = Modifier.fillMaxWidth().then(searchScroll.searchBarScrollBehaviorModifier)
                                    .padding(horizontal = 16.dp, vertical = 8.dp)
                                    .border(1.dp, MaterialTheme.colorScheme.outlineVariant, SearchBarDefaults.inputFieldShape),
                                colors = SearchBarDefaults.colors(containerColor = MaterialTheme.colorScheme.surfaceContainerLowest),
                                tonalElevation = 0.dp,
                                inputField = {
                                    SearchBarDefaults.InputField(state = rememberTextFieldState(), onSearch = {},
                                        expanded = false, onExpandedChange = { if (it) navigator.open(Screen.Search, org.json.JSONObject()) },
                                        readOnly = true, modifier = Modifier.testTag("open_search"),
                                        placeholder = { Text(ui(R.string.search_titles)) },
                                        leadingIcon = { Icon(painterResource(R.drawable.ic_search), null) })
                                })

                            if (page == AppPage.Favorites) {
                                val mediaTabs = listOf(null) + AppPage.catalogs
                                PrimaryTabRow(selectedTabIndex = mediaTabs.indexOf(savedCatalog)) {
                                    mediaTabs.forEach { item ->
                                        Tab(selected = savedCatalog == item, onClick = { savedCatalog = item },
                                            text = { Text(if (item == null) ui(R.string.choice_all) else stringResource(item.title),
                                                style = MaterialTheme.typography.labelMedium, maxLines = 1) },
                                            modifier = Modifier.height(40.dp).testTag("saved_catalog_${item?.name ?: "All"}"))
                                    }
                                }
                                if (wide) PersonalListTabs(savedList, { savedList = it }, secondary = true)
                            }
                        }
                    },
                    bottomBar = {
                        Column {
                            if (page == AppPage.Favorites && !wide) PersonalListTabs(savedList, { savedList = it })
                            if (page == AppPage.Catalogs && !wide) {
                                PrimaryTabRow(selectedTabIndex = AppPage.catalogs.indexOf(catalog)) {
                                    AppPage.catalogs.forEach { item ->
                                        Tab(selected = catalog == item, onClick = { catalog = item },
                                            selectedContentColor = MaterialTheme.colorScheme.primary,
                                            unselectedContentColor = MaterialTheme.colorScheme.onSurfaceVariant,
                                            text = { Text(stringResource(item.title), style = MaterialTheme.typography.labelMedium) },
                                            modifier = Modifier.height(40.dp).testTag("catalog_${item.name}"))
                                    }
                                }
                            }
                        if (!wide) NavigationBar(Modifier.testTag("mobile_bottom_bar")) {
                            AppPage.mobileNavigation.forEach { destination ->
                                NavigationBarItem(selected = compactPage == destination, onClick = { page = destination },
                                    icon = { Icon(painterResource(destination.icon), null) },
                                    label = { Text(stringResource(destination.title)) },
                                    modifier = Modifier.testTag("nav_${destination.name}"))
                            }
                        }
                        }
                    },
                ) { padding ->
                    Box(Modifier.fillMaxSize().padding(padding).consumeWindowInsets(padding)) {
                        AnimatedContent(page, label = "main_navigation", transitionSpec = {
                            if (initialState == AppPage.Search || targetState == AppPage.Search)
                                EnterTransition.None togetherWith ExitTransition.None
                            else fadeIn(tween()) togetherWith fadeOut(tween())
                        }) { shownPage ->
                            if (shownPage != page && (shownPage == AppPage.Search || page == AppPage.Search)) return@AnimatedContent
                            if (shownPage == AppPage.Catalogs) {
                                Box(Modifier.fillMaxSize().testTag("page_Catalogs")) { CatalogScreen(catalog, false) }
                            } else if (shownPage == AppPage.Favorites) {
                                Box(Modifier.fillMaxSize().testTag("page_Favorites")) {
                                    PersonalScreen(savedList.key, false, savedCatalog?.mediaType)
                                }
                            } else PageContent(shownPage, isTv = false, onBack = { page = AppPage.Home })
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun PersonalListTabs(selected: PersonalList, onSelect: (PersonalList) -> Unit, secondary: Boolean = false) {
    val tabs: @Composable () -> Unit = {
        PersonalList.entries.forEach { item ->
            Tab(selected = selected == item, onClick = { onSelect(item) },
                text = { Text(item.label, style = MaterialTheme.typography.labelMedium) },
                modifier = Modifier.height(40.dp).testTag("saved_${item.name}"))
        }
    }
    if (secondary) SecondaryScrollableTabRow(selectedTabIndex = PersonalList.entries.indexOf(selected), edgePadding = 12.dp) { tabs() }
    else PrimaryScrollableTabRow(selectedTabIndex = PersonalList.entries.indexOf(selected), edgePadding = 12.dp) { tabs() }
}
