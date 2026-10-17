package net.anibel.app

import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.layout.*
import androidx.compose.material3.carousel.HorizontalCenteredHeroCarousel
import androidx.compose.material3.carousel.rememberCarouselState
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
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
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.Dp
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
    val slides = coreData(CoreCommand.Slider, json("limit" to 6), revision)
    val context = LocalContext.current
    val tablet = !isTv && LocalConfiguration.current.screenWidthDp >= 600
    val slideSize = if (tablet)
        Modifier.fillMaxWidth().height(240.dp) else Modifier.fillMaxWidth().aspectRatio(16f / 9f)
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
                DataContent(slides, isTv, { revision++ }, loadingModifier = slideSize) { result ->
                    val available = (result as? JSONArray).objects().filter { it.text("img") !in unavailableImages }
                    val slideContent: @Composable (JSONObject, Modifier) -> Unit = { slide, modifier ->
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
                                AppImage(slide.text("img"), null, slideSize
                                    .clip(MaterialTheme.shapes.large), contentScale = ContentScale.Crop,
                                    onError = { slide.text("img")?.let { unavailableImages = unavailableImages + it } })
                                ListItem(headlineContent = { Text(slide.localized("title"), maxLines = 2) },
                                    trailingContent = { Icon(painterResource(R.drawable.ic_back), null, Modifier.rotate(180f)) },
                                    colors = ListItemDefaults.colors(containerColor = MaterialTheme.colorScheme.background))
                            }
                        }
                        if (isTv) androidx.tv.material3.Surface(click, Modifier.width(320.dp)) { body() }
                        else Card(click, modifier = modifier, colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.background)) { body() }
                    }
                    if (isTv) LazyRow(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                        items(available) { slideContent(it, Modifier) }
                    } else if (available.isNotEmpty()) HorizontalCenteredHeroCarousel(
                        state = rememberCarouselState { available.size }, itemSpacing = 12.dp,
                        maxItemWidth = if (tablet) 480.dp else Dp.Unspecified,
                        modifier = Modifier.fillMaxWidth().testTag("home_carousel")) { index ->
                        slideContent(available[index], Modifier.maskClip(MaterialTheme.shapes.large))
                    }
                }

            }
        }
    }
}

@Composable
internal fun PagedTitles(op: CoreCommand, args: JSONObject, isTv: Boolean, reload: Boolean = false, refreshHeader: (() -> Unit)? = null, header: @Composable () -> Unit = {}) {
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
    LazyVerticalGrid(titleGridCells(isTv), Modifier.fillMaxSize(), state = grid,
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
internal fun SearchScreen(isTv: Boolean, onBack: () -> Unit) {
    var input by rememberSaveable { mutableStateOf("") }
    var query by rememberSaveable { mutableStateOf("") }
    var revision by remember { mutableIntStateOf(0) }
    val core = LocalCore.current
    val (action, run) = rememberAction()
    val keyboard = LocalSoftwareKeyboardController.current
    val focus = remember { FocusRequester() }
    val close = { keyboard?.hide(); onBack() }
    val submit = {
        query = input.trim()
        keyboard?.hide()
        if (query.isNotBlank()) run { core.value(CoreCommand.SearchHistory, json("action" to "add", "query" to query)); revision++ }
    }
    LaunchedEffect(Unit) { focus.requestFocus() }
    // The in-layout Material search keeps tablet navigation available. The collapsed-only
    // SearchBar(state = ...) deliberately disables the software keyboard.
    SearchBar(expanded = true, onExpandedChange = { if (!it) close() },
        modifier = Modifier.fillMaxWidth().testTag("search_bar"), windowInsets = WindowInsets(0, 0, 0, 0),
        colors = SearchBarDefaults.colors(containerColor = MaterialTheme.colorScheme.surfaceContainerLowest), tonalElevation = 0.dp,
        inputField = {
            SearchBarDefaults.InputField(query = input, onQueryChange = { input = it },
                onSearch = { submit() }, expanded = true, onExpandedChange = { if (!it) close() },
                modifier = Modifier.focusRequester(focus).testTag("search_input"),
                placeholder = { Text(ui(R.string.search_titles)) },
                leadingIcon = {
                    if (!isTv) IconButton(close, Modifier.testTag("back")) {
                        Icon(painterResource(R.drawable.ic_back), ui(R.string.back))
                    } else Icon(painterResource(R.drawable.ic_search), null)
                },
                trailingIcon = { if (input.isNotBlank()) IconButton({ input = ""; query = "" }) {
                    Icon(painterResource(R.drawable.ic_close), ui(R.string.remove))
                } })
        }) {
        Column(Modifier.fillMaxSize().imePadding().padding(horizontal = if (isTv) 24.dp else 16.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp)) {
            if (isTv) AppButton(ui(R.string.search), true, submit, enabled = input.isNotBlank())
            if (query.isBlank()) {
                AppText(ui(R.string.recent_searches), isTv)
                val history = coreData(CoreCommand.SearchHistory, revision = revision)
                DataContent(history, isTv, { revision++ }) { value ->
                    (value as? JSONArray).strings().forEach { item ->
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            if (isTv) AppButton(item, true, { input = item; query = item }, Modifier.weight(1f))
                            else TextButton({ input = item; query = item }, Modifier.weight(1f)) { Text(item, Modifier.fillMaxWidth()) }
                            AppButton(ui(R.string.remove), isTv, { run { core.value(CoreCommand.SearchHistory, json("action" to "remove", "query" to item)); revision++ } })
                        }
                    }
                }
            } else {
                val results = coreData(CoreCommand.Search, json("query" to query, "limit" to 100), revision)
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
}
