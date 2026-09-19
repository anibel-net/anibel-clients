package net.anibel.app

import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.Alignment
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.grid.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import org.json.JSONArray
import org.json.JSONObject

@OptIn(androidx.tv.material3.ExperimentalTvMaterial3Api::class)
@Composable
internal fun ChoiceRow(choices: List<String>, selected: String, isTv: Boolean, selectOnFocus: Boolean = false, select: (String) -> Unit) {
    LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp), contentPadding = PaddingValues(8.dp)) {
        items(choices) { value ->
            if (isTv) androidx.tv.material3.FilterChip(selected == value, { select(value) },
                modifier = Modifier.onFocusChanged { if (selectOnFocus && it.isFocused) select(value) }) { androidx.tv.material3.Text(keyLabel(value)) }
            else FilterChip(selected == value, { select(value) }, label = { Text(keyLabel(value)) })
        }
    }
}

@Composable
internal fun HomeScreen(isTv: Boolean) {
    val navigator = LocalNavigator.current
    var revision by remember { mutableIntStateOf(0) }
    val slides = coreData("slider", json("limit" to 6), revision)
    val context = LocalContext.current
    var unavailableImages by remember(revision) { mutableStateOf(emptySet<String>()) }
    Column(Modifier.fillMaxSize()) {
        if (isTv) Row(Modifier.fillMaxWidth().padding(horizontal = 24.dp, vertical = 16.dp),
            horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
            Text(ui(R.string.nav_home), Modifier.weight(1f), style = MaterialTheme.typography.headlineSmall)
            AppButton(ui(R.string.search), isTv, { navigator.open(Screen.Search, JSONObject()) })
        }
        key(revision) {
            SectionedUpdates(isTv, reload = revision > 0, refresh = { revision++ }) {
                if (isTv) Text(ui(R.string.featured), style = MaterialTheme.typography.titleLarge)
                DataContent(slides, isTv, { revision++ }, loadingModifier = Modifier.fillMaxWidth().aspectRatio(16f / 9f)) { result ->
                    val available = (result as? JSONArray).objects().filter { it.text("img") !in unavailableImages }
                    val slideContent: @Composable (JSONObject) -> Unit = { slide ->
                        val click = {
                            slide.text("link")?.let { link ->
                                val uri = Uri.parse(link)
                                val segments = uri.pathSegments
                                if (segments.size >= 2 && segments[0] in AppPage.catalogs.map { it.mediaType })
                                    navigator.open(Screen.Detail, json("mediaType" to segments[0], "slug" to segments[1]))
                                else if (uri.scheme == "https" || uri.scheme == "http")
                                    runCatching { context.startActivity(Intent(Intent.ACTION_VIEW, uri)) }
                            }
                            Unit
                        }
                        val body: @Composable () -> Unit = {
                            Column {
                                AppImage(slide.text("img"), null, Modifier.fillMaxWidth().aspectRatio(16f / 9f)
                                    .clip(MaterialTheme.shapes.large), contentScale = ContentScale.Crop,
                                    onError = { slide.text("img")?.let { unavailableImages = unavailableImages + it } })
                                ListItem(headlineContent = { Text(slide.localized("title"), maxLines = 2) },
                                    trailingContent = { Icon(painterResource(R.drawable.ic_back), null, Modifier.rotate(180f)) },
                                    colors = ListItemDefaults.colors(containerColor = MaterialTheme.colorScheme.background))
                            }
                        }
                        if (isTv) androidx.tv.material3.Surface(click, Modifier.width(320.dp)) { body() }
                        else Card(click, colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.background)) { body() }
                    }
                    if (isTv) LazyRow(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                        items(available) { slideContent(it) }
                    } else if (available.isNotEmpty()) HorizontalPager(
                        state = rememberPagerState { available.size },
                        contentPadding = PaddingValues(end = 16.dp), pageSpacing = 12.dp,
                        modifier = Modifier.fillMaxWidth().testTag("home_carousel")) { index -> slideContent(available[index]) }
                }

            }
        }
    }
}

@Composable
internal fun PagedTitles(op: String, args: JSONObject, isTv: Boolean, reload: Boolean = false, refreshHeader: (() -> Unit)? = null, header: @Composable () -> Unit = {}) {
    var offset by remember { mutableLongStateOf(0) }
    var retry by remember { mutableIntStateOf(if (reload) 1 else 0) }
    var titles by remember { mutableStateOf(emptyList<JSONObject>()) }
    val data = coreData(op, JSONObject(args.toString()).put("offset", offset).put("limit", 24), retry)
    LaunchedEffect(data) {
        if (data is RemoteData.Ready) {
            val docs = (data.value as? JSONObject)?.optJSONArray("docs").objects()
            titles = ((if (offset == 0L) emptyList() else titles) + docs).distinctBy { it.optString("mediaId") + it.optString("updateType") + it.optString("num") }
        }
    }
    val grid = rememberLazyGridState()
    LaunchedEffect(data, titles.size) {
        val page = (data as? RemoteData.Ready)?.value as? JSONObject ?: return@LaunchedEffect
        val next = page.optLong("nextOffset")
        if (!page.optBoolean("hasMore") || next <= offset || titles.isEmpty()) return@LaunchedEffect
        snapshotFlow { (grid.layoutInfo.visibleItemsInfo.lastOrNull()?.index ?: -1) >= titles.size - 6 }
            .collect { nearEnd -> if (nearEnd) offset = next }
    }
    if (titles.isEmpty() && data == RemoteData.Loading) {
        LoadingContent(Modifier.fillMaxSize())
        return
    }
    RefreshablePage(isTv, data == RemoteData.Loading && retry > 0, { offset = 0; retry++; refreshHeader?.invoke() }, Modifier.fillMaxSize()) {
    LazyVerticalGrid(GridCells.Adaptive(if (isTv) 124.dp else 108.dp), Modifier.fillMaxSize(), state = grid,
        contentPadding = PaddingValues(if (isTv) 24.dp else 20.dp),
        horizontalArrangement = Arrangement.spacedBy(16.dp), verticalArrangement = Arrangement.spacedBy(20.dp)) {
        item(span = { GridItemSpan(maxLineSpan) }) { Column(verticalArrangement = Arrangement.spacedBy(16.dp)) { header() } }
        items(titles) { TitleCard(CatalogTitle(it), isTv) }
        item(span = { GridItemSpan(maxLineSpan) }) {
            DataContent(data, isTv, { retry++ }) { value ->
                if (titles.isEmpty()) AppText(ui(R.string.empty_titles), isTv)

            }
        }
    }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun SearchScreen(isTv: Boolean, onBack: (() -> Unit)? = null) {
    var input by rememberSaveable { mutableStateOf("") }
    var query by rememberSaveable { mutableStateOf("") }
    var revision by remember { mutableIntStateOf(0) }
    val core = LocalCore.current
    val (action, run) = rememberAction()
    val keyboard = LocalSoftwareKeyboardController.current
    val submit = {
        query = input.trim()
        keyboard?.hide()
        if (query.isNotBlank()) run { core.value("searchHistory", json("action" to "add", "query" to query)); revision++ }
    }
    Column(Modifier.fillMaxSize().padding(horizontal = if (isTv) 24.dp else 16.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
        SearchBar(state = rememberSearchBarState(), modifier = Modifier.fillMaxWidth(), inputField = {
            SearchBarDefaults.InputField(query = input, onQueryChange = { input = it },
                onSearch = { submit() }, expanded = false, onExpandedChange = {},
                placeholder = { Text(ui(R.string.search_titles)) },
                leadingIcon = {
                    if (!isTv && onBack != null) IconButton(onBack, Modifier.testTag("back")) {
                        Icon(painterResource(R.drawable.ic_back), ui(R.string.back))
                    } else Icon(painterResource(R.drawable.ic_search), null)
                },
                trailingIcon = { if (input.isNotBlank()) IconButton({ input = ""; query = "" }) {
                    Icon(painterResource(R.drawable.ic_close), ui(R.string.remove))
                } })
        })
        if (isTv) AppButton(ui(R.string.search), true, submit, enabled = input.isNotBlank())
        if (query.isBlank()) {
            AppText(ui(R.string.recent_searches), isTv)
            val history = coreData("searchHistory", revision = revision)
            DataContent(history, isTv, { revision++ }) { value ->
                (value as? JSONArray).strings().forEach { item ->
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        if (isTv) AppButton(item, true, { input = item; query = item }, Modifier.weight(1f))
                        else TextButton({ input = item; query = item }, Modifier.weight(1f)) { Text(item, Modifier.fillMaxWidth()) }
                        AppButton(ui(R.string.remove), isTv, { run { core.value("searchHistory", json("action" to "remove", "query" to item)); revision++ } })
                    }
                }
            }
        } else {
            val results = coreData("search", json("query" to query, "limit" to 100), revision)
            RefreshablePage(isTv, results == RemoteData.Loading && revision > 0, { revision++ }, Modifier.weight(1f)) {
                DataContent(results, isTv, { revision++ }, loadingModifier = Modifier.fillMaxSize()) {
                    key(query) {
                        TitleGrid(it as? JSONArray, isTv, contentPadding = PaddingValues(top = 12.dp, bottom = 24.dp), showCount = !isTv)
                    }
                }
            }
        }
        ActionStatus(action, isTv)
    }
}
