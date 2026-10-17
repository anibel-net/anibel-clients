package net.anibel.app

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.clickable
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
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.draw.clip
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.Alignment
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.unit.dp
import org.json.JSONObject

internal enum class DetailTab { About, Content, Comments, Related }

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun DetailScreen(args: JSONObject, isTv: Boolean, onBack: () -> Unit = {}) {
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
            Column(Modifier.fillMaxSize().verticalScroll(scroll),
                verticalArrangement = Arrangement.spacedBy(24.dp)) {
                BoxWithConstraints(Modifier.fillMaxWidth()) {
                    val main: @Composable () -> Unit = {
                        DetailAbout(detail, false, { scope.launch { contentPosition.bringIntoView() }; Unit }) { revision++ }
                        Column(Modifier.bringIntoViewRequester(contentPosition), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                            SectionHeading(ui(when (CatalogTitle(detail).type) {
                                "anime", "cinema" -> R.string.episodes
                                "manga" -> R.string.chapters
                                else -> R.string.content_tab
                            }), Modifier.padding(horizontal = 24.dp))
                            DetailContent(detail, false)
                        }
                        SectionHeading(ui(R.string.comments_tab), Modifier.padding(horizontal = 24.dp))
                        CommentsScreen(detail, false)
                    }
                    if (maxWidth >= 840.dp) Row {
                        Column(Modifier.weight(2f), verticalArrangement = Arrangement.spacedBy(24.dp)) { main() }
                        Column(Modifier.weight(1f).padding(top = 24.dp)) { DetailRelatedTitles(detail) }
                    } else Column(verticalArrangement = Arrangement.spacedBy(24.dp)) {
                        main()
                        DetailRelatedTitles(detail)
                    }
                }
                Spacer(Modifier.height(24.dp))
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

@Composable
private fun DetailRelatedTitles(media: JSONObject) {
    val navigator = LocalNavigator.current
    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(20.dp)) {
        listOf(R.string.related_titles to "relations", R.string.recommendations to "recommendations").forEach { (label, key) ->
            val titles = media.optJSONArray(key)
            if (titles.objects().isNotEmpty()) {
                Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    SectionHeading(ui(label), Modifier.padding(horizontal = 24.dp)) {
                        navigator.open(Screen.Collection, json("title" to ui(label), "items" to titles))
                    }
                    if (LocalConfiguration.current.screenWidthDp >= 600) {
                        Column {
                            titles.objects().forEach { value ->
                                val card = CatalogTitle(value)
                                ListItem(headlineContent = { Text(card.title, maxLines = 2) },
                                    supportingContent = { Text(listOf(card.meta, card.info).filter(String::isNotBlank).joinToString("\n")) },
                                    leadingContent = {
                                        AppImage(card.poster, null, Modifier.width(64.dp).aspectRatio(2f / 3f).clip(MaterialTheme.shapes.medium),
                                            contentScale = ContentScale.Crop)
                                    },
                                    colors = ListItemDefaults.colors(containerColor = androidx.compose.ui.graphics.Color.Transparent),
                                    modifier = Modifier.clickable {
                                        navigator.open(Screen.Detail, json("slug" to card.slug, "mediaType" to card.type))
                                    })
                            }
                        }
                    } else TitleRow(titles, false)
                }
            }
        }
    }
}
