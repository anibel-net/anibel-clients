package net.anibel.app

import androidx.compose.foundation.layout.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.compose.material3.Text as MobileText
import androidx.tv.material3.Text as TvText

@Composable
internal fun PageContent(page: AppPage, isTv: Boolean, modifier: Modifier = Modifier) {
    if (page in AppPage.catalogs) {
        CatalogScreen(page, isTv, modifier)
        return
    }
    Column(modifier.fillMaxSize().testTag("page_${page.name}")) {
        if (isTv && page != AppPage.Home && page !in AppPage.catalogs) androidx.tv.material3.Text(stringResource(page.title),
            Modifier.padding(24.dp), style = androidx.tv.material3.MaterialTheme.typography.headlineSmall)
        when (page) {
            AppPage.Home -> HomeScreen(isTv)
            AppPage.Favorites -> if (isTv) TvLibraryScreen() else PersonalScreen("favorites", false)
            AppPage.Profile -> ProfileScreen(isTv, edgeToEdge = !isTv)
            AppPage.Download -> DownloadsScreen(isTv)
            AppPage.Settings -> SettingsScreen(isTv)
            else -> CatalogScreen(page, isTv)
        }
    }
}

@Composable
internal fun AppText(text: String, isTv: Boolean, modifier: Modifier = Modifier, maxLines: Int = Int.MAX_VALUE) {
    if (isTv) TvText(text, modifier, maxLines = maxLines, overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis)
    else MobileText(text, modifier, maxLines = maxLines, overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis)
}

@Composable
internal fun AppButton(text: String, isTv: Boolean, onClick: () -> Unit, modifier: Modifier = Modifier, enabled: Boolean = true) {
    if (isTv) androidx.tv.material3.Button(onClick, modifier, enabled = enabled, contentPadding = PaddingValues(horizontal = 16.dp, vertical = 8.dp)) { TvText(text, style = androidx.tv.material3.MaterialTheme.typography.labelLarge) }
    else androidx.compose.material3.FilledTonalButton(onClick, modifier, enabled = enabled) { MobileText(text) }
}
