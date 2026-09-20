package net.anibel.app

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.relocation.BringIntoViewRequester
import androidx.compose.foundation.relocation.bringIntoViewRequester
import kotlinx.coroutines.launch
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.Alignment
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.unit.dp
import org.json.JSONObject

internal enum class DetailTab { About, Content, Comments, Related }

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun DetailScreen(args: JSONObject, isTv: Boolean, onBack: () -> Unit = {}) {
    val navigator = LocalNavigator.current
    var revision by remember { mutableIntStateOf(0) }
    val media = coreData(CoreCommand.Media, args, revision)
    val scroll = rememberScrollState()
    Column(Modifier.fillMaxSize()) {
        if (!isTv) TopAppBar(
            title = {
                androidx.compose.animation.AnimatedVisibility(scroll.value > 0) {
                    Text(((media as? RemoteData.Ready)?.value as? JSONObject)?.let { CatalogTitle(it).title }.orEmpty(),
                        modifier = Modifier.testTag("detail_toolbar_title"), maxLines = 1, overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis)
                }
            }, navigationIcon = {
                IconButton(onBack, Modifier.testTag("back")) { Icon(painterResource(R.drawable.ic_back), ui(R.string.back)) }
            }, actions = { TitleOptions(args, (media as? RemoteData.Ready)?.value as? JSONObject) },
            windowInsets = WindowInsets(0, 0, 0, 0))
        RefreshablePage(isTv, media == RemoteData.Loading && revision > 0, { revision++ }, Modifier.weight(1f)) {
    DataContent(media, isTv, { revision++ }, loadingModifier = Modifier.fillMaxSize()) { value ->
        val detail = value as? JSONObject
        if (detail == null) AppText(ui(R.string.title_missing), isTv)
        else if (!isTv) {
            val contentPosition = remember { BringIntoViewRequester() }
            val scope = rememberCoroutineScope()
            Column(Modifier.fillMaxSize().verticalScroll(scroll), horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(24.dp)) {
                BoxWithConstraints(Modifier.widthIn(max = 1080.dp).fillMaxWidth()) {
                    val about: @Composable () -> Unit = {
                        DetailAbout(detail, false, { scope.launch { contentPosition.bringIntoView() }; Unit }) { revision++ }
                    }
                    val episodes: @Composable () -> Unit = {
                        Column(Modifier.bringIntoViewRequester(contentPosition), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                            Text(ui(R.string.content_tab), Modifier.padding(horizontal = 24.dp), style = MaterialTheme.typography.titleLarge)
                            DetailContent(detail, false)
                        }
                    }
                    if (maxWidth >= 840.dp) Row {
                        Column(Modifier.weight(1f)) { about() }
                        Column(Modifier.weight(1f).padding(top = 24.dp)) { episodes() }
                    } else Column(verticalArrangement = Arrangement.spacedBy(24.dp)) { about(); episodes() }
                }
                Column(Modifier.widthIn(max = 1080.dp).fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(20.dp)) {
                    HorizontalDivider(Modifier.padding(horizontal = 24.dp))
                    Text(ui(R.string.comments_tab), Modifier.padding(horizontal = 24.dp), style = MaterialTheme.typography.titleLarge)
                    CommentsScreen(detail, false)
                    if (detail.optJSONArray("relations").objects().isNotEmpty()) {
                        SectionHeading(ui(R.string.related_titles), Modifier.padding(horizontal = 24.dp)) {
                        navigator.open(Screen.Collection, json("title" to ui(R.string.related_titles), "items" to detail.optJSONArray("relations")))
                    }
                        TitleRow(detail.optJSONArray("relations"), false)
                    }
                    if (detail.optJSONArray("recommendations").objects().isNotEmpty()) {
                        SectionHeading(ui(R.string.recommendations), Modifier.padding(horizontal = 24.dp)) {
                        navigator.open(Screen.Collection, json("title" to ui(R.string.recommendations), "items" to detail.optJSONArray("recommendations")))
                    }
                        TitleRow(detail.optJSONArray("recommendations"), false)
                    }
                    Spacer(Modifier.height(24.dp))
                }
            }
        }
        else Column(Modifier.fillMaxSize()) {
            var tab by rememberSaveable { mutableStateOf(DetailTab.About) }
            ChoiceRow(DetailTab.entries.map { it.name }, tab.name, true, selectOnFocus = true) { tab = DetailTab.valueOf(it) }
            when (tab) {
                DetailTab.About -> PageColumn { DetailAbout(detail, true, { tab = DetailTab.Content }) { revision++ } }
                DetailTab.Content -> DetailContent(detail, isTv)
                DetailTab.Comments -> CommentsScreen(detail, isTv)
                DetailTab.Related -> PageColumn {
                    detail.text("franchise")?.let { AppText(it, isTv) }
                    AppText(ui(R.string.related_titles), isTv)
                    TitleRow(detail.optJSONArray("relations"), isTv)
                    AppText(ui(R.string.recommendations), isTv)
                    TitleRow(detail.optJSONArray("recommendations"), isTv)
                }
            }
        }
    }
    }
    }
}
