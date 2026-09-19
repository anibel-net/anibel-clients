package net.anibel.app

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.input.key.KeyEventType
import androidx.compose.ui.input.key.key
import androidx.compose.ui.input.key.type
import androidx.compose.ui.input.key.onPreviewKeyEvent
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.foundation.focusGroup
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.Density
import androidx.compose.ui.platform.LocalDensity
import androidx.tv.material3.*

private const val DrawerScale = 0.8f

class TvActivity : ComponentActivity() {
    override fun attachBaseContext(base: android.content.Context) { super.attachBaseContext(Language.wrap(base)) }
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent { AppHost(true) { TvApp() } }
    }
}

@Composable
internal fun TvApp() {
    var page by rememberSaveable { mutableStateOf(AppPage.Home) }
    val drawerState = rememberDrawerState(DrawerValue.Open)
    var navigationHasFocus by remember { mutableStateOf(false) }
    val contentFocus = remember { FocusRequester() }
    val navigationFocus = remember {
        AppPage.tvNavigation.associateWith { FocusRequester() }
    }
    LaunchedEffect(Unit) { navigationFocus.getValue(page).requestFocus() }

    // Back first returns from content to the selected sidebar item, then goes up.
    BackHandler(enabled = !navigationHasFocus || page != AppPage.Home) {
        if (navigationHasFocus) page = AppPage.Home
        navigationFocus.getValue(page).requestFocus()
    }

    SystemTheme(isTv = true) {
        ModalNavigationDrawer(
            drawerState = drawerState,
            modifier = Modifier.fillMaxSize().background(MaterialTheme.colorScheme.background).safeDrawingPadding(),
            drawerContent = { value ->
                val density = LocalDensity.current
                // TV drawer items have fixed native dimensions. Scale only the drawer,
                // preserving the device font scale and the library focus/animation behavior.
                CompositionLocalProvider(LocalDensity provides Density(density.density * DrawerScale, density.fontScale)) {
                    Column(
                        Modifier.fillMaxHeight().background(MaterialTheme.colorScheme.surface)
                            .padding(horizontal = 12.dp, vertical = 20.dp)
                            .onFocusChanged { navigationHasFocus = it.hasFocus }
                            .onPreviewKeyEvent {
                                if (it.key == Key.DirectionRight && it.type == KeyEventType.KeyDown) {
                                    contentFocus.requestFocus()
                                    true
                                } else false
                            }
                            .focusGroup(),
                        verticalArrangement = Arrangement.spacedBy(4.dp),
                    ) {
                        AppPage.tvNavigation.forEach { destination ->
                            if (destination == AppPage.Profile) Spacer(Modifier.weight(1f).heightIn(min = 12.dp))
                            NavigationDrawerItem(
                                selected = page == destination,
                                onClick = { page = destination },
                                leadingContent = { Icon(painterResource(destination.icon), if (value == DrawerValue.Closed) stringResource(destination.title) else null) },
                                modifier = Modifier.focusRequester(navigationFocus.getValue(destination))
                                    .onFocusChanged { if (it.isFocused) page = destination }
                                    .testTag("nav_${destination.name}"),
                            ) {
                                Text(stringResource(destination.title), maxLines = 1)
                            }
                        }
                    }
                }
            },
        ) {
            // Reserve the native collapsed item width plus the drawer's horizontal padding.
            Box(Modifier.fillMaxSize().padding(start = 80.dp * DrawerScale).focusRequester(contentFocus).focusGroup()) {
                key(page) { PageContent(page, isTv = true) }
            }
        }
    }
}
