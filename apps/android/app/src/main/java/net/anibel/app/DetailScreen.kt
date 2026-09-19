package net.anibel.app

import android.text.Html
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.relocation.BringIntoViewRequester
import androidx.compose.foundation.relocation.bringIntoViewRequester
import kotlinx.coroutines.launch
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.Alignment
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.unit.dp
import org.json.JSONArray
import org.json.JSONObject

internal enum class DetailTab { About, Content, Comments, Related }

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun DetailScreen(args: JSONObject, isTv: Boolean, onBack: () -> Unit = {}) {
    val navigator = LocalNavigator.current
    var revision by remember { mutableIntStateOf(0) }
    val media = coreData("media", args, revision)
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

@Composable
private fun DetailAbout(media: JSONObject, isTv: Boolean, openContent: (() -> Unit)?, refresh: () -> Unit) {
    val core = LocalCore.current
    val navigator = LocalNavigator.current
    val signedIn = LocalSession.current.optBoolean("authenticated")
    val card = CatalogTitle(media)
    val (action, run) = rememberAction()
    val kind = coreData("mediaKind", json("mediaType" to card.type))
    val context = androidx.compose.ui.platform.LocalContext.current
    val startContent: () -> Unit = {
        run {
            when (card.type) {
                "anime", "cinema" -> {
                    val saved = context.getSharedPreferences("playback", android.content.Context.MODE_PRIVATE)
                        .getString("last:${card.type}:${card.id}", null)?.let { runCatching { JSONObject(it) }.getOrNull() }
                    val episodes = core.call("episodeChoices", json("mediaId" to card.id, "kind" to (saved?.text("episodeType") ?: "dub")))
                        .optJSONArray("items").objects()
                    val episode = episodes.firstOrNull { it.text("id") == saved?.text("episodeId") }
                        ?: episodes.firstOrNull { !it.optBoolean("watched") } ?: episodes.firstOrNull()
                    if (episode == null) action.message = ui(R.string.no_episodes)
                    else navigator.open(Screen.Player, json("url" to episode.text("url"), "episodeId" to episode.text("id"),
                        "episodeType" to episode.text("type"), "mediaId" to card.id, "mediaType" to card.type))
                }
                "manga" -> {
                    val chapters = core.call("chapters", json("mediaId" to card.id, "limit" to 1000)).optJSONArray("docs").objects()
                        .map { it.getDouble("chapter") }.distinct().sorted()
                    val saved = context.getSharedPreferences("reader", android.content.Context.MODE_PRIVATE)
                        .getString("chapter:${card.slug}", null)?.toDoubleOrNull()
                    val chapter = saved?.takeIf { it in chapters } ?: chapters.firstOrNull()
                    if (chapter == null) action.message = ui(R.string.no_chapters)
                    else navigator.open(Screen.Reader, json("slug" to card.slug, "chapter" to chapter, "chapters" to JSONArray(chapters)))
                }
                else -> openContent?.invoke()
            }
        }
    }
    Column(Modifier.padding(if (isTv) 0.dp else 24.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
        if (!isTv) {
            Row(horizontalArrangement = Arrangement.spacedBy(20.dp), verticalAlignment = Alignment.Top) {
                AppImage(card.poster, null, Modifier.width(80.dp).aspectRatio(2f / 3f).clip(MaterialTheme.shapes.medium), contentScale = ContentScale.Crop)
                Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    Text(card.title, style = MaterialTheme.typography.headlineSmall)
                    media.optJSONObject("title")?.text("en")?.takeIf { it != card.title }?.let {
                        Text(it, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                    Text(media.optJSONArray("genres").strings().joinToString(" · ", transform = ::keyLabel),
                        style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Text(keyLabel(card.type), style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.primary)
                    val production = listOfNotNull(media.text("studio"), media.text("country")?.let(::keyLabel)).joinToString(" · ")
                    if (production.isNotBlank()) Text(production, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
            Row(Modifier.fillMaxWidth().padding(vertical = 8.dp), horizontalArrangement = Arrangement.SpaceEvenly,
                verticalAlignment = Alignment.CenterVertically) {
                val facts = listOfNotNull(media.text("year"),
                    media.optDouble("rating").takeIf { it.isFinite() && it > 0 }?.let { "★ %.1f / 5".format(it / 2) },
                    media.text("status")?.let(::keyLabel))
                facts.forEachIndexed { index, fact ->
                    if (index > 0) VerticalDivider(Modifier.height(24.dp))
                    Text(fact, Modifier.weight(1f).padding(horizontal = 8.dp), style = MaterialTheme.typography.labelLarge,
                        textAlign = androidx.compose.ui.text.style.TextAlign.Center)
                }
            }
            if (openContent != null) Button(startContent, Modifier.fillMaxWidth().heightIn(min = 48.dp), enabled = !action.busy) {
                Icon(painterResource(if (card.type in listOf("anime", "cinema")) R.drawable.ic_play else if (card.type == "manga") R.drawable.ic_book else R.drawable.ic_download), null)
                Spacer(Modifier.width(8.dp))
                Text(ui(if (card.type in listOf("anime", "cinema")) R.string.play else if (card.type == "manga") R.string.read else R.string.download))
            }
            if (signedIn) {
                PersonalTitleActions(media, (kind as? RemoteData.Ready)?.value as? JSONObject, action.busy,
                    favorite = { run { core.value("setFavorite", json("mediaId" to card.id, "mediaType" to card.type,
                        "selected" to !media.optBoolean("favorite"))); refresh() } },
                    progress = { status -> run { core.value("setMark", json("mediaId" to card.id, "mediaType" to card.type,
                        "status" to status, "current" to media.optJSONObject("mark")?.text("status"))); refresh() } },
                    rate = { rating -> run { core.value("setRating", json("mediaId" to card.id, "mediaType" to card.type,
                        "rating" to rating * 2)); refresh() } })
                ActionStatus(action, false)
            } else TextButton({ navigator.open(Screen.Profile, JSONObject()) }) { Text(ui(R.string.sign_in_progress)) }
            if (card.type in listOf("anime", "cinema")) EpisodePreview(card.id, isTv)
        } else Box(Modifier.fillMaxWidth().clip(MaterialTheme.shapes.large)) {
            media.text("wallpaper")?.let { artwork ->
                AppImage(artwork, null, Modifier.matchParentSize().alpha(0.18f), contentScale = ContentScale.Crop)
            }
        Row(Modifier.fillMaxWidth().padding(16.dp), horizontalArrangement = Arrangement.spacedBy(32.dp), verticalAlignment = Alignment.CenterVertically) {
            AppImage(card.poster, null, Modifier.width(112.dp).aspectRatio(2f / 3f)
                .clip(MaterialTheme.shapes.large), contentScale = ContentScale.Crop)
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(card.title, style = MaterialTheme.typography.headlineMedium)
                media.optJSONObject("title")?.text("en")?.takeIf { it != card.title }?.let {
                    Text(it, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                Text(card.meta, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                if (card.info.isNotBlank()) Text(card.info, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                if (openContent != null) AppButton(ui(if (card.type in listOf("anime", "cinema")) R.string.play else if (card.type == "manga") R.string.read else R.string.download), isTv, startContent, enabled = !action.busy)
            }
        }
        }
        if (isTv) {
            if (signedIn) PersonalTitleActions(media, (kind as? RemoteData.Ready)?.value as? JSONObject, action.busy,
                favorite = { run { core.value("setFavorite", json("mediaId" to card.id, "mediaType" to card.type,
                    "selected" to !media.optBoolean("favorite"))); refresh() } },
                progress = { status -> run { core.value("setMark", json("mediaId" to card.id, "mediaType" to card.type,
                    "status" to status, "current" to media.optJSONObject("mark")?.text("status"))); refresh() } },
                rate = { rating -> run { core.value("setRating", json("mediaId" to card.id, "mediaType" to card.type,
                    "rating" to rating * 2)); refresh() } }, isTv = true)
            else AppButton(ui(R.string.sign_in_progress), true, { navigator.open(Screen.Profile, JSONObject()) })
            ActionStatus(action, true)
        }
        if (isTv) FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            media.optJSONArray("genres").strings().forEach { genre ->
                Text(keyLabel(genre), style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
        val description = Html.fromHtml(media.localized("description"), Html.FROM_HTML_MODE_COMPACT).toString().trim()
        if (description.isNotBlank()) {
            Text(ui(R.string.about), style = MaterialTheme.typography.titleLarge)
            Text(description, style = MaterialTheme.typography.bodyLarge, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        val credits = listOfNotNull(media.text("studio"), media.text("country")?.let(::keyLabel)).joinToString(" · ")
        if (isTv && credits.isNotBlank()) Text(credits, style = MaterialTheme.typography.labelLarge)

    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun DetailContent(media: JSONObject, isTv: Boolean) {
    val card = CatalogTitle(media)
    val core = LocalCore.current
    val navigator = LocalNavigator.current
    val (action, run) = rememberAction()
    var revision by remember { mutableIntStateOf(0) }
    val kind = coreData("mediaKind", json("mediaType" to card.type))
    val enqueue = rememberDownloadEnqueue()
    DataContent(kind, isTv, { revision++ }) { kindValue ->
        when ((kindValue as? JSONObject)?.optString("content")) {
            "episodes" -> {
                var language by rememberSaveable { mutableStateOf("dub") }
                var resource by rememberSaveable { mutableStateOf("all_sources") }
                val choices = coreData("episodeChoices", json("mediaId" to card.id, "kind" to language,
                    "resource" to resource.toLongOrNull()), revision)
                Column(Modifier.fillMaxWidth().padding(horizontal = 24.dp)) {
                    DataContent(choices, isTv, { revision++ }) { value ->
                        val data = value as? JSONObject ?: JSONObject()
                        val kinds = data.optJSONArray("kinds").strings()
                        if (isTv && kinds.size > 1) ChoiceRow(kinds, data.optString("selectedKind"), true, selectOnFocus = true) { language = it }
                        else if (kinds.size > 1) PrimaryTabRow(kinds.indexOf(data.optString("selectedKind")).coerceAtLeast(0)) {
                            kinds.forEach { value ->
                                Tab(selected = value == data.optString("selectedKind"), onClick = { language = value },
                                    text = { Text(keyLabel(value)) },
                                    selectedContentColor = MaterialTheme.colorScheme.primary,
                                    unselectedContentColor = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                        }
                        if (kinds.size > 1) Spacer(Modifier.height(16.dp))
                        val resources = data.optJSONArray("resources").strings()
                        if (resources.size > 1) ChoiceRow(listOf("all_sources") + resources, resource, isTv) { resource = it }
                        ActionStatus(action, isTv)
                        val episodes = data.optJSONArray("items").objects()
                        if (episodes.isEmpty()) AppText(ui(R.string.no_episodes), isTv)
                        DetailItems(episodes, isTv, dividers = false, grid = isTv) { episode ->
                            val screenshots = episodeScreenshots(episode.text("url"))
                            val screenshot = screenshots?.getOrNull(screenshots.size / 2)
                            if (!isTv) MobileEpisodeCard(
                                episode = episode, screenshot = screenshot, screenshotLoading = screenshots == null, busy = action.busy,
                                signedIn = LocalSession.current.optBoolean("authenticated"),
                                play = { navigator.open(Screen.Player, json("url" to episode.text("url"),
                                    "episodeId" to episode.text("id"), "episodeType" to episode.text("type"), "mediaId" to card.id, "mediaType" to card.type)) },
                                download = { run {
                                    enqueue("video", downloadRequest(media).put("episodeUrl", episode.text("url"))
                                        .put("episodeId", episode.text("id")).put("episodeType", episode.text("type"))
                                        .put("episodeLabel", ui(R.string.episode_number, episode.optString("episode").removeSuffix(".0"))))
                                    action.message = ui(R.string.download_added)
                                } },
                                watched = { run {
                                    core.value("setWatched", json("entityId" to episode.text("id"), "selected" to !episode.optBoolean("watched")))
                                    revision++
                                } }) else Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                                    androidx.tv.material3.Surface(onClick = { navigator.open(Screen.Player, json("url" to episode.text("url"),
                                        "episodeId" to episode.text("id"), "episodeType" to episode.text("type"), "mediaId" to card.id, "mediaType" to card.type)) },
                                        modifier = Modifier.fillMaxWidth(),
                                        colors = androidx.tv.material3.ClickableSurfaceDefaults.colors(containerColor = androidx.tv.material3.MaterialTheme.colorScheme.background)) {
                                        Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                                            if (screenshot != null) AppImage(screenshot, null, Modifier.fillMaxWidth().aspectRatio(16f / 9f)
                                                .clip(MaterialTheme.shapes.medium), contentScale = ContentScale.Crop)
                                            else Box(Modifier.fillMaxWidth().aspectRatio(16f / 9f), contentAlignment = Alignment.Center) {
                                                Icon(painterResource(R.drawable.ic_play), null)
                                            }
                                            Text(ui(R.string.episode_number, episode.optString("episode").removeSuffix(".0")), style = MaterialTheme.typography.titleSmall)
                                            episode.text("title")?.takeIf { it.isNotBlank() }?.let {
                                                Text(it, maxLines = 2, style = MaterialTheme.typography.bodySmall,
                                                    overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis)
                                            }
                                        }
                                    }
                                    FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                                        AppButton(ui(R.string.download), isTv, { run {
                                            enqueue("video", downloadRequest(media).put("episodeUrl", episode.text("url"))
                                                .put("episodeId", episode.text("id")).put("episodeType", episode.text("type"))
                                                .put("episodeLabel", ui(R.string.episode_number, episode.optString("episode").removeSuffix(".0"))))
                                            action.message = ui(R.string.download_added)
                                        } }, enabled = !action.busy)
                                        if (LocalSession.current.optBoolean("authenticated")) AppButton(if (episode.optBoolean("watched")) ui(R.string.watched_checked) else ui(R.string.mark_watched), isTv, {
                                            run { core.value("setWatched", json("entityId" to episode.text("id"), "selected" to !episode.optBoolean("watched"))); revision++ }
                                        }, enabled = !action.busy)
                                    }
                                }
                        }
                    }
                }
            }
            "chapters" -> {
                val chapters = coreData("chapters", json("mediaId" to card.id, "limit" to 1000), revision)
                DataContent(chapters, isTv, { revision++ }) { value ->
                    val rows = (value as? JSONObject)?.optJSONArray("docs").objects()
                    val numbers = JSONArray(rows.map { it.getDouble("chapter") }.distinct().sorted())
                    Column(Modifier.padding(horizontal = 24.dp)) {
                        if (rows.isEmpty()) AppText(ui(R.string.no_chapters), isTv)
                        ActionStatus(action, isTv)
                        DetailItems(rows, isTv) { chapter ->
                            if (!isTv) ListItem(
                                headlineContent = { Text(ui(R.string.chapter_number, chapter.optString("chapter"))) },
                                supportingContent = { chapter.text("title")?.takeIf { it.isNotBlank() }?.let { Text(it) } },
                                leadingContent = { Icon(painterResource(R.drawable.ic_book), null) },
                                trailingContent = { IconButton({ run {
                                    enqueue("manga", downloadRequest(media).put("chapter", chapter.getDouble("chapter"))
                                        .put("chapterId", chapter.text("id")).put("chapterTitle", chapter.text("title")).put("chapterList", numbers))
                                    action.message = ui(R.string.download_added)
                                } }, enabled = !action.busy) { Icon(painterResource(R.drawable.ic_download), ui(R.string.download)) } },
                                modifier = Modifier.clickable { navigator.open(Screen.Reader, json("slug" to card.slug,
                                    "chapter" to chapter.getDouble("chapter"), "chapters" to numbers)) }
                            ) else Column {
                                AppText(ui(R.string.chapter_number, chapter.optString("chapter")) + " · " + chapter.text("title").orEmpty(), isTv)
                                Row {
                                    AppButton(ui(R.string.read), isTv, { navigator.open(Screen.Reader, json("slug" to card.slug,
                                        "chapter" to chapter.getDouble("chapter"), "chapters" to numbers)) })
                                    AppButton(ui(R.string.download), isTv, { run {
                                        enqueue("manga", downloadRequest(media).put("chapter", chapter.getDouble("chapter"))
                                            .put("chapterId", chapter.text("id")).put("chapterTitle", chapter.text("title")).put("chapterList", numbers))
                                        action.message = ui(R.string.download_added)
                                    } }, enabled = !action.busy)
                                }
                            }
                        }
                    }
                }
            }
            else -> Column(Modifier.padding(24.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                val instructions = media.localized("instructions")
                if (instructions.isNotBlank()) AppText(Html.fromHtml(instructions, Html.FROM_HTML_MODE_COMPACT).toString(), isTv)
                if (media.text("download") == null) AppText(ui(R.string.no_download), isTv)
                else AppButton(ui(R.string.download_file), isTv, { run {
                    enqueue("file", downloadRequest(media).put("fileUrl", media.text("download")))
                    action.message = ui(R.string.download_added)
                } }, enabled = !action.busy)
                ActionStatus(action, isTv)
            }
        }
    }
}

private fun downloadRequest(media: JSONObject) = json("mediaId" to media.text("mediaId"),
    "mediaType" to media.text("mediaType"), "slug" to media.text("slug"), "title" to media.localized("title"), "posterUrl" to media.text("poster"))

@Composable
private fun CommentsScreen(media: JSONObject, isTv: Boolean) {
    var revision by remember { mutableIntStateOf(0) }
    var offset by rememberSaveable { mutableLongStateOf(0) }
    var draft by rememberSaveable { mutableStateOf("") }
    var reply by remember { mutableStateOf<JSONObject?>(null) }
    val core = LocalCore.current
    val (action, run) = rememberAction()
    val args = json("mediaId" to media.text("mediaId"), "mediaType" to media.text("mediaType"))
    val comments = coreData("comments", JSONObject(args.toString()).put("offset", offset).put("limit", 20), revision)
    val composer = remember { BringIntoViewRequester() }
    val scope = rememberCoroutineScope()
    Column(Modifier.fillMaxWidth().padding(horizontal = 24.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
        Column(Modifier.bringIntoViewRequester(composer), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        if (LocalSession.current.optBoolean("authenticated")) {
            reply?.let { target ->
                AppText(ui(R.string.reply_to, target.optJSONObject("user")?.optString("username").orEmpty()), isTv)
                AppButton(ui(R.string.cancel_reply), isTv, { reply = null }, enabled = !action.busy)
            }
            OutlinedTextField(draft, { draft = it }, Modifier.fillMaxWidth(), placeholder = { Text(ui(R.string.comment)) }, shape = MaterialTheme.shapes.large, minLines = 2, maxLines = 6, enabled = !action.busy)
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.End) {
            AppButton(ui(R.string.send), isTv, { run {
                val request = JSONObject(args.toString()).put("content", draft.trim())
                reply?.text("id")?.let { request.put("replyTo", it) }
                core.value("addComment", request)
                draft = ""; reply = null; offset = 0; revision++
            } }, enabled = !action.busy && draft.isNotBlank())
            }
        } else AppText(ui(R.string.sign_in_comment), isTv)
        ActionStatus(action, isTv)
        }
        DataContent(comments, isTv, { revision++ }) { value ->
            val page = value as? JSONObject ?: JSONObject()
            Column {
                val rows = page.optJSONArray("docs").objects()
                if (rows.isEmpty()) Text(ui(R.string.empty_comments), style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant)
                DetailItems(rows, isTv, dividers = false) { comment -> CommentItem(comment, isTv, 0) {
                    if (!action.busy) { reply = it; scope.launch { composer.bringIntoView() } }
                } }
                Row {
                    if (offset > 0) AppButton(ui(R.string.previous), isTv, { offset = (offset - 20).coerceAtLeast(0) })
                    if (offset + 20 < page.optLong("totalDocs")) AppButton(ui(R.string.next), isTv, { offset += 20 })
                }
            }
        }
    }
}

/** Mobile items share the page scroll; only visible batches are composed. */
@Composable
private fun DetailItems(rows: List<JSONObject>, isTv: Boolean, dividers: Boolean = true, grid: Boolean = false, content: @Composable (JSONObject) -> Unit) {
    if (isTv && grid) androidx.compose.foundation.lazy.grid.LazyVerticalGrid(
        androidx.compose.foundation.lazy.grid.GridCells.Adaptive(200.dp),
        contentPadding = PaddingValues(8.dp), horizontalArrangement = Arrangement.spacedBy(16.dp),
        verticalArrangement = Arrangement.spacedBy(20.dp)) {
        items(rows.size) { content(rows[it]) }
    } else if (isTv) LazyColumn(contentPadding = PaddingValues(8.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
        items(rows) { content(it) }
    } else {
        var visible by rememberSaveable(rows.size) { mutableIntStateOf(8) }
        Column(verticalArrangement = Arrangement.spacedBy(if (dividers) 8.dp else 16.dp)) {
            rows.take(visible).forEach { item ->
                Column(Modifier.fillMaxWidth()) { content(item) }
                if (dividers) HorizontalDivider()
            }
            if (visible < rows.size) OutlinedButton({ visible = (visible + 8).coerceAtMost(rows.size) }, Modifier.fillMaxWidth()) { Text(ui(R.string.load_more)) }
        }
    }
}
