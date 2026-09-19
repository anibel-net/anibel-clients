package net.anibel.app

import androidx.compose.foundation.layout.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp

@OptIn(androidx.tv.material3.ExperimentalTvMaterial3Api::class)
@Composable
internal fun TvLibraryScreen() {
    var list by rememberSaveable { mutableStateOf(PersonalList.Favorites) }
    var catalog by rememberSaveable { mutableStateOf(AppPage.Anime) }
    Column(Modifier.fillMaxSize()) {
        Row(Modifier.padding(horizontal = 24.dp, vertical = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            PersonalList.entries.forEach { item ->
                androidx.tv.material3.FilterChip(selected = list == item, onClick = { list = item },
                    modifier = Modifier.testTag("saved_${item.name}").onFocusChanged { if (it.isFocused) list = item }) {
                    androidx.tv.material3.Text(item.label)
                }
            }
        }
        Row(Modifier.padding(horizontal = 24.dp, vertical = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            AppPage.catalogs.forEach { item ->
                androidx.tv.material3.FilterChip(selected = catalog == item, onClick = { catalog = item },
                    modifier = Modifier.testTag("saved_catalog_${item.name}").onFocusChanged { if (it.isFocused) catalog = item }) {
                    androidx.tv.material3.Text(ui(item.title))
                }
            }
        }
        Box(Modifier.weight(1f)) { PersonalScreen(list.key, true, catalog.mediaType, showHeader = false) }
    }
}
