package net.anibel.app

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.grid.*
import androidx.compose.material3.Text
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import org.json.JSONObject

internal enum class UpdateSection(val labelId: Int) {
    SUB(R.string.choice_sub), DUB(R.string.choice_dub), CINEMA(R.string.catalog_cinema),
    MANGA(R.string.catalog_manga), GAMES(R.string.catalog_games)
}

private data class UpdateBatch(val section: UpdateSection, val offset: Long,
    val titles: List<JSONObject>, val next: Long?)

/** One vertical grid; older batches append after all sections in the current batch. */
@Composable
internal fun SectionedUpdates(isTv: Boolean, reload: Boolean, refresh: () -> Unit, header: @Composable () -> Unit) {
    val core = LocalCore.current
    val navigator = LocalNavigator.current
    var batches by remember { mutableStateOf(emptyList<UpdateBatch>()) }
    var state by remember { mutableStateOf<RemoteData>(RemoteData.Loading) }
    var retry by remember { mutableIntStateOf(0) }
    val grid = rememberLazyGridState()
    suspend fun loadNext() {
        state = RemoteData.Loading
        try {
            val requests = UpdateSection.entries.mapNotNull { section ->
                val last = batches.lastOrNull { it.section == section }
                val offset = if (last == null) 0L else last.next ?: return@mapNotNull null
                section to offset
            }
            val next = coroutineScope {
                requests.map { (section, offset) -> async {
                    val page = core.call("updatesPage", json("type" to section.name, "offset" to offset, "limit" to 6), reload = reload)
                    val nextOffset = page.optLong("nextOffset")
                    UpdateBatch(section, offset, page.optJSONArray("docs").objects(),
                        nextOffset.takeIf { page.optBoolean("hasMore") && it > offset })
                } }.awaitAll()
            }
            batches = batches + next
            state = RemoteData.Ready(Unit)
        } catch (cancelled: CancellationException) { throw cancelled }
        catch (failure: Exception) { state = RemoteData.Failed(failure.message ?: ui(R.string.load_page_failed)) }
    }
    LaunchedEffect(retry) {
        loadNext()
        snapshotFlow {
            val hasMore = UpdateSection.entries.any { section -> batches.lastOrNull { it.section == section }?.next != null }
            state is RemoteData.Ready && hasMore && grid.isScrollInProgress &&
                (grid.layoutInfo.visibleItemsInfo.lastOrNull()?.index ?: -1) >= grid.layoutInfo.totalItemsCount - 5
        }.collect { nearEnd -> if (nearEnd) loadNext() }
    }
    if (batches.isEmpty() && state == RemoteData.Loading) {
        LoadingContent(Modifier.fillMaxSize())
        return
    }
    RefreshablePage(isTv, reload && state == RemoteData.Loading && batches.isEmpty(), refresh, Modifier.fillMaxSize()) {
        LazyVerticalGrid(GridCells.Adaptive(if (isTv) 124.dp else 108.dp), Modifier.fillMaxSize().testTag("home_updates"),
            state = grid, contentPadding = PaddingValues(if (isTv) 24.dp else 20.dp),
            horizontalArrangement = Arrangement.spacedBy(16.dp), verticalArrangement = Arrangement.spacedBy(20.dp)) {
            item(key = "featured", span = { GridItemSpan(maxLineSpan) }) { header() }
            batches.forEach { batch ->
                if (batch.titles.isNotEmpty() || batch.offset == 0L) {
                    item(key = "${batch.section}:${batch.offset}", span = { GridItemSpan(maxLineSpan) }) {
                        SectionHeading(ui(batch.section.labelId), Modifier.testTag("updates_${batch.section}_${batch.offset}"), isTv = isTv) {
                            navigator.open(Screen.Collection, json("title" to ui(batch.section.labelId), "op" to "updatesPage", "type" to batch.section.name))
                        }
                    }
                    if (batch.titles.isEmpty()) item(span = { GridItemSpan(maxLineSpan) }) {
                        Text(ui(R.string.empty_titles), style = MaterialTheme.typography.bodyMedium)
                    }
                    items(batch.titles) { TitleCard(CatalogTitle(it), isTv) }
                }
            }
            item(key = "load_updates", span = { GridItemSpan(maxLineSpan) }) {
                DataContent(state, isTv, { retry++ }) {}
            }
        }
    }
}
