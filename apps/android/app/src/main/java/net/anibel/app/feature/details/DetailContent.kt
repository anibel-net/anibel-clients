package net.anibel.app

import android.text.Html
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.draw.clip
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.unit.dp
import org.json.JSONArray
import org.json.JSONObject

@Composable
internal fun DetailContent(media: JSONObject, isTv: Boolean) {
    val card = CatalogTitle(media)
    val core = LocalCore.current
    val navigator = LocalNavigator.current
    val (action, run) = rememberAction()
    var revision by remember { mutableIntStateOf(0) }
    val kind = coreData(CoreCommand.MediaKind, json("mediaType" to card.type))
    val enqueue = rememberDownloadEnqueue()
    DataContent(kind, isTv, { revision++ }) { kindValue ->
        when ((kindValue as? JSONObject)?.optString("content")) {
            "episodes" -> {
                var language by rememberSaveable { mutableStateOf("dub") }
                var resource by rememberSaveable { mutableStateOf("all_sources") }
                val choices = coreData(CoreCommand.EpisodeChoices, json("mediaId" to card.id, "kind" to language,
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
                                    core.value(CoreCommand.SetWatched, json("entityId" to episode.text("id"), "selected" to !episode.optBoolean("watched")))
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
                                            run { core.value(CoreCommand.SetWatched, json("entityId" to episode.text("id"), "selected" to !episode.optBoolean("watched"))); revision++ }
                                        }, enabled = !action.busy)
                                    }
                                }
                        }
                    }
                }
            }
            "chapters" -> {
                val chapters = coreData(CoreCommand.Chapters, json("mediaId" to card.id, "limit" to 1000), revision)
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

internal fun downloadRequest(media: JSONObject) = json("mediaId" to media.text("mediaId"),
    "mediaType" to media.text("mediaType"), "slug" to media.text("slug"), "title" to media.localized("title"), "posterUrl" to media.text("poster"))
