package net.anibel.app

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.CancellationException
import org.json.JSONObject

internal enum class UpdateSection(val labelId: Int) {
    SUB(R.string.choice_sub), DUB(R.string.choice_dub), CINEMA(R.string.catalog_cinema),
    MANGA(R.string.catalog_manga), GAMES(R.string.catalog_games)
}

private class UpdatesRowState {
    var titles by mutableStateOf(emptyList<JSONObject>())
    var nextOffset by mutableStateOf<Long?>(0L)
    var response by mutableStateOf<RemoteData>(RemoteData.Loading)
}

@Composable
internal fun SectionedUpdates(isTv: Boolean, reload: Boolean, refresh: () -> Unit, header: @Composable () -> Unit) {
    val rows = remember { UpdateSection.entries.associateWith { UpdatesRowState() } }
    RefreshablePage(isTv, false, refresh, Modifier.fillMaxSize()) {
        LazyColumn(Modifier.fillMaxSize().testTag("home_updates"),
            contentPadding = if (isTv) PaddingValues(24.dp) else PaddingValues(vertical = 20.dp),
            verticalArrangement = Arrangement.spacedBy(24.dp)) {
            item(key = "featured") {
                if (isTv) header() else Box(Modifier.padding(horizontal = 20.dp)) { header() }
            }
            items(UpdateSection.entries, key = { it.name }) { section ->
                UpdatesRow(section, rows.getValue(section), isTv, reload)
            }
        }
    }
}

@Composable
private fun UpdatesRow(section: UpdateSection, data: UpdatesRowState, isTv: Boolean, reload: Boolean) {
    val core = LocalCore.current
    val navigator = LocalNavigator.current
    val row = rememberLazyListState()
    var retry by remember { mutableIntStateOf(0) }
    val cardWidth = titleRowWidth(isTv)
    val edgePadding = if (isTv) 0.dp else 20.dp

    suspend fun loadNext() {
        val offset = data.nextOffset ?: return
        data.response = RemoteData.Loading
        try {
            val page = core.call(CoreCommand.UpdatesPage,
                json("type" to section.name, "offset" to offset, "limit" to 6), reload = reload)
            data.titles = (data.titles + page.optJSONArray("docs").objects()).distinctBy {
                listOf(it.optString("mediaId"), it.optString("updateType"), it.optString("num"))
            }
            data.nextOffset = page.optLong("nextOffset").takeIf { page.optBoolean("hasMore") && it > offset }
            data.response = RemoteData.Ready(Unit)
        } catch (cancelled: CancellationException) { throw cancelled }
        catch (error: Exception) { data.response = RemoteData.Failed(error.message ?: ui(R.string.load_page_failed)) }
    }

    LaunchedEffect(section, retry) {
        if (data.response == RemoteData.Loading || retry > 0) loadNext()
        snapshotFlow {
            // Keep the cursor in the observed value so a fast response can trigger another page.
            data.nextOffset?.takeIf {
                data.response is RemoteData.Ready &&
                    (row.layoutInfo.visibleItemsInfo.lastOrNull()?.index ?: -1) >= data.titles.size - 3
            }
        }.collect { offset -> if (offset != null) loadNext() }
    }

    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        SectionHeading(ui(section.labelId), Modifier.padding(horizontal = edgePadding).testTag("updates_${section.name}_0"), isTv = isTv) {
            navigator.open(Screen.Collection, json("title" to ui(section.labelId), "op" to "updatesPage", "type" to section.name))
        }
        LazyRow(state = row, modifier = Modifier.fillMaxWidth().testTag("updates_row_${section.name}"),
            contentPadding = PaddingValues(horizontal = edgePadding),
            horizontalArrangement = Arrangement.spacedBy(16.dp)) {
            items(data.titles) { TitleCard(CatalogTitle(it), isTv, Modifier.width(cardWidth)) }
            if (data.titles.isNotEmpty() && data.response == RemoteData.Loading) item {
                LoadingContent(Modifier.width(64.dp).height(cardWidth * 1.5f))
            }
        }
        when (val result = data.response) {
            RemoteData.Loading -> if (data.titles.isEmpty()) LinearProgressIndicator(Modifier.fillMaxWidth().padding(horizontal = edgePadding))
            is RemoteData.Failed -> {
                AppText(result.message, isTv, Modifier.padding(horizontal = edgePadding))
                AppButton(ui(R.string.try_again), isTv, { retry++ }, Modifier.padding(horizontal = edgePadding))
            }
            is RemoteData.Ready -> if (data.titles.isEmpty()) AppText(ui(R.string.empty_titles), isTv, Modifier.padding(horizontal = edgePadding))
        }
    }
}
