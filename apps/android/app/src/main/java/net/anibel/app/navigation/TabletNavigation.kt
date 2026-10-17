package net.anibel.app

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.launch

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun TabletNavigation(selected: AppPage, onSelect: (AppPage) -> Unit) {
    val state = rememberWideNavigationRailState()
    val scope = rememberCoroutineScope()
    val expanded = state.targetValue == WideNavigationRailValue.Expanded
    val toggleLabel = stringResource(if (expanded) R.string.collapse_navigation else R.string.expand_navigation)
    val stateLabel = stringResource(if (state.currentValue == WideNavigationRailValue.Expanded)
        R.string.navigation_expanded else R.string.navigation_collapsed)
    WideNavigationRail(state = state, contentPadding = PaddingValues(0.dp),
        modifier = Modifier.widthIn(max = 220.dp).testTag("tablet_navigation")) {
        Column(Modifier.fillMaxHeight()) {
            TooltipBox(positionProvider = TooltipDefaults.rememberTooltipPositionProvider(TooltipAnchorPosition.Above),
                tooltip = { PlainTooltip { Text(toggleLabel) } }, state = rememberTooltipState(),
                modifier = Modifier.fillMaxWidth().wrapContentWidth(if (expanded) Alignment.Start else Alignment.CenterHorizontally)) {
                IconButton(onClick = { scope.launch { state.toggle() } },
                    modifier = Modifier.testTag("toggle_tablet_navigation").semantics { stateDescription = stateLabel }) {
                    Icon(painterResource(R.drawable.ic_menu), toggleLabel)
                }
            }
            Column(Modifier.weight(1f).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                (listOf(AppPage.Home, AppPage.Search) + AppPage.catalogs).forEach { destination ->
                    TabletNavigationItem(destination, selected, expanded, onSelect)
                }
            }
            Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                listOf(AppPage.Profile, AppPage.Favorites, AppPage.Download, AppPage.Settings).forEach { destination ->
                    TabletNavigationItem(destination, selected, expanded, onSelect)
                }
            }
        }
    }
}

@Composable
private fun TabletNavigationItem(destination: AppPage, selected: AppPage, expanded: Boolean, onSelect: (AppPage) -> Unit) {
    WideNavigationRailItem(
        selected = selected == destination,
        onClick = { onSelect(destination) },
        railExpanded = expanded,
        label = { Text(stringResource(destination.title)) },
        icon = { Icon(painterResource(destination.icon), null) },
        modifier = Modifier.testTag("nav_${destination.name}"),
    )
}
