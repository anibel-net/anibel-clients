package net.anibel.app

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.grid.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import androidx.compose.foundation.clickable
import androidx.compose.foundation.focusGroup
import androidx.compose.ui.focus.FocusDirection
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusProperties
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.focus.onFocusChanged
import androidx.lifecycle.viewmodel.compose.viewModel

@Composable
internal fun CatalogScreen(catalog: AppPage, isTv: Boolean, modifier: Modifier = Modifier, model: CatalogModel = viewModel()) {
    LaunchedEffect(catalog) { model.open(catalog) }
    DisposableEffect(model) { onDispose { model.stop() } }
    var showFilters by rememberSaveable(catalog) { mutableStateOf(false) }
    val filterFocus = remember { FocusRequester() }

    RefreshablePage(isTv, model.state == LoadState.Loading && model.titles.isNotEmpty(), { model.load(reload = true) }, modifier.fillMaxSize()) {
    Box(Modifier.fillMaxSize()) {
    Column(Modifier.fillMaxSize().padding(horizontal = if (isTv) 24.dp else 16.dp)
        .focusProperties { onEnter = { if (isTv) filterFocus.requestFocus() } }.focusGroup()
        .testTag("page_${catalog.name}")) {
        if (isTv) Row(Modifier.fillMaxWidth().padding(vertical = 8.dp), verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            AppButton(if (model.selected.isEmpty()) ui(R.string.filters) else ui(R.string.filters_count, model.selected.values.sumOf { it.size }),
                isTv, { showFilters = true }, Modifier.focusRequester(filterFocus).testTag("filters"))
            if (isTv) androidx.tv.material3.Text(stringResource(catalog.title), Modifier.weight(1f),
                style = androidx.tv.material3.MaterialTheme.typography.titleLarge)
            AppText(ui(R.string.titles_count, model.total), isTv, if (isTv) Modifier else Modifier.weight(1f))
        }
        key(catalog, model.selected) {
            val grid = rememberLazyGridState()
            LaunchedEffect(grid) {
                snapshotFlow {
                    model.state == LoadState.Ready && model.hasMore && model.titles.isNotEmpty() &&
                        (grid.layoutInfo.visibleItemsInfo.lastOrNull()?.index ?: -1) >= model.titles.size - 10
                }.collect { nearEnd -> if (nearEnd) model.load() }
            }
            LazyVerticalGrid(GridCells.Adaptive(if (isTv) 124.dp else 108.dp), Modifier.weight(1f).testTag("titles"),
                state = grid,
                contentPadding = PaddingValues(top = 12.dp, bottom = if (isTv) 12.dp else 88.dp), horizontalArrangement = Arrangement.spacedBy(16.dp),
                verticalArrangement = Arrangement.spacedBy(20.dp)) {
                if (!isTv && model.state != LoadState.Loading) item(span = { GridItemSpan(maxLineSpan) }) {
                    Text(ui(R.string.titles_count, model.total), style = MaterialTheme.typography.labelLarge,
                        color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                items(model.titles, key = { it.id }) { title ->
                    TitleCard(title, isTv)
                }
                item(span = { GridItemSpan(maxLineSpan) }) {
                    Column(Modifier.fillMaxWidth().padding(8.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                        when (model.state) {
                            LoadState.Loading -> if (model.titles.isNotEmpty()) LoadingContent(Modifier.testTag("loading"))
                            LoadState.Failed -> {
                                AppText(model.error, isTv, Modifier.testTag("catalog_error"))
                                AppButton(ui(R.string.try_again), isTv, { model.load(reload = true) })
                            }
                            LoadState.Ready -> if (model.titles.isEmpty()) AppText(ui(R.string.no_matches), isTv)
                        }
                    }
                }
            }
        }
    }
    if (model.state == LoadState.Loading && model.titles.isEmpty()) LoadingContent(Modifier.fillMaxSize().testTag("loading"))
    if (!isTv) ExtendedFloatingActionButton(onClick = { showFilters = true },
        modifier = Modifier.align(Alignment.BottomEnd).padding(16.dp).testTag("filters"),
        icon = { Icon(painterResource(R.drawable.ic_filter), null) },
        text = { Text(if (model.selected.isEmpty()) ui(R.string.filters) else ui(R.string.filters_count, model.selected.values.sumOf { it.size })) })
    }
    }
    if (showFilters) FiltersDialog(model, isTv) { showFilters = false }

}

@Composable
private fun TvFiltersDialog(model: CatalogModel, close: () -> Unit) {
    var active by rememberSaveable { mutableStateOf(FilterKey.Type) }
    val selectedKey = active.takeIf { it in model.options } ?: model.options.keys.firstOrNull()
    val categories = remember { FilterKey.entries.associateWith { FocusRequester() } }
    val choices = remember { FocusRequester() }
    Dialog(onDismissRequest = close, properties = DialogProperties(usePlatformDefaultWidth = false)) {
        androidx.tv.material3.Surface(Modifier.width(720.dp).height(440.dp), shape = MaterialTheme.shapes.extraLarge) {
            Column(Modifier.padding(24.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
                AppText(ui(R.string.filters), true)
                when (model.filterState) {
                    LoadState.Loading -> LinearProgressIndicator(Modifier.fillMaxWidth())
                    LoadState.Failed -> {
                        AppText(model.filterError, true)
                        AppButton(ui(R.string.try_again), true, model::loadFilters)
                    }
                    LoadState.Ready -> Unit
                }
                Row(Modifier.weight(1f), horizontalArrangement = Arrangement.spacedBy(24.dp)) {
                    LazyColumn(Modifier.width(200.dp).focusProperties {
                        onExit = {
                            if (requestedFocusDirection == FocusDirection.Right && model.options[selectedKey].orEmpty().isNotEmpty())
                                choices.requestFocus()
                        }
                    }.focusGroup(), contentPadding = PaddingValues(4.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        items(model.options.keys.toList(), key = { it.name }) { category ->
                            val count = model.selected[category].orEmpty().size
                            androidx.tv.material3.Surface(selectedKey == category, { active = category },
                                Modifier.fillMaxWidth().focusRequester(categories.getValue(category))
                                    .onFocusChanged { if (it.isFocused) active = category }
                                    .testTag("filter_category_${category.name}")) {
                                AppText(category.label + if (count > 0) " · $count" else "", true,
                                    Modifier.padding(horizontal = 12.dp, vertical = 10.dp))
                            }
                        }
                    }
                    key(selectedKey) {
                        LazyColumn(Modifier.weight(1f).focusRequester(choices).focusProperties {
                            onExit = {
                                if (requestedFocusDirection == FocusDirection.Left)
                                    selectedKey?.let { categories.getValue(it).requestFocus() }
                            }
                        }.focusGroup().testTag("filter_options"), contentPadding = PaddingValues(4.dp),
                            verticalArrangement = Arrangement.spacedBy(8.dp)) {
                            val category = selectedKey
                            if (category != null) items(model.options[category].orEmpty(), key = { it }) { value ->
                                val selected = value in model.selected[category].orEmpty()
                                androidx.tv.material3.Surface(selected, { model.toggle(category, value) }, Modifier.fillMaxWidth()) {
                                    Row(Modifier.padding(horizontal = 12.dp, vertical = 10.dp),
                                        verticalAlignment = Alignment.CenterVertically) {
                                        AppText(keyLabel(value), true, Modifier.weight(1f))
                                        AppText(if (selected) "✓" else "", true, Modifier.width(24.dp))
                                    }
                                }
                            }
                        }
                    }
                }
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp),
                    verticalAlignment = Alignment.CenterVertically) {
                    AppButton(ui(R.string.clear_filters), true, model::clear, enabled = model.selected.isNotEmpty())
                    Spacer(Modifier.weight(1f))
                    AppButton(ui(R.string.done), true, close)
                }
            }
        }
        LaunchedEffect(model.options.keys.toList()) {
            withFrameNanos { }
            selectedKey?.let { categories.getValue(it).requestFocus() }
        }
    }
}

@Composable
private fun FiltersDialog(model: CatalogModel, isTv: Boolean, close: () -> Unit) {
    if (isTv) {
        TvFiltersDialog(model, close)
        return
    }
    var active by rememberSaveable { mutableStateOf(FilterKey.Genre) }
    var query by rememberSaveable { mutableStateOf("") }
    val selectedKey = active.takeIf { it in model.options } ?: model.options.keys.firstOrNull()
    Dialog(onDismissRequest = close, properties = DialogProperties(usePlatformDefaultWidth = false)) {
        Surface(Modifier.fillMaxSize(), shape = androidx.compose.ui.graphics.RectangleShape) {
            Column(Modifier.safeDrawingPadding().padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                    IconButton(close) { Icon(painterResource(R.drawable.ic_back), ui(R.string.back)) }
                    Text(ui(R.string.filters), Modifier.weight(1f), style = MaterialTheme.typography.headlineSmall)
                    TextButton(model::clear, enabled = model.selected.isNotEmpty()) { Text(ui(R.string.clear_filters)) }
                }
                when (model.filterState) {
                    LoadState.Loading -> LinearProgressIndicator(Modifier.fillMaxWidth())
                    LoadState.Failed -> { Text(model.filterError); AppButton(ui(R.string.try_again), false, model::loadFilters) }
                    LoadState.Ready -> Unit
                }
                ChoiceRow(model.options.keys.map { it.label }, selectedKey?.label.orEmpty(), isTv) { label ->
                    active = model.options.keys.first { it.label == label }; query = ""
                }
                OutlinedTextField(query, { query = it }, Modifier.fillMaxWidth(), singleLine = true,
                    placeholder = { Text(ui(R.string.search)) },
                    leadingIcon = { Icon(painterResource(R.drawable.ic_search), null) })
                LazyColumn(Modifier.weight(1f).testTag("filter_options"), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    val key = selectedKey
                    if (key != null) items(model.options[key].orEmpty().filter { keyLabel(it).contains(query, ignoreCase = true) }) { value ->
                        val selected = value in model.selected[key].orEmpty()
                        ListItem(
                            headlineContent = { Text(keyLabel(value)) },
                            trailingContent = { if (key.single) RadioButton(selected, null) else Checkbox(selected, null) },
                            modifier = Modifier.fillMaxWidth().clickable { model.toggle(key, value) },
                            colors = ListItemDefaults.colors(containerColor = if (selected) MaterialTheme.colorScheme.secondaryContainer else MaterialTheme.colorScheme.surface))
                    }
                }
                Button(close, Modifier.fillMaxWidth().heightIn(min = 52.dp)) { Text(ui(R.string.done) + " · " + ui(R.string.titles_count, model.total)) }
            }
        }
    }
}
