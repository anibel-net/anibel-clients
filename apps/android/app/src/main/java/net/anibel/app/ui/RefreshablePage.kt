package net.anibel.app

import androidx.compose.foundation.layout.Box
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun RefreshablePage(isTv: Boolean, refreshing: Boolean, refresh: () -> Unit,
    modifier: Modifier = Modifier, content: @Composable () -> Unit) {
    if (isTv) Box(modifier) { content() }
    else PullToRefreshBox(isRefreshing = refreshing, onRefresh = refresh, modifier = modifier) { content() }
}
