package net.anibel.app

import android.text.Html
import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.unit.dp
import org.json.JSONObject

@Composable
internal fun DetailAbout(media: JSONObject, isTv: Boolean, openContent: (() -> Unit)?, refresh: () -> Unit) {
    val core = LocalCore.current
    val navigator = LocalNavigator.current
    val signedIn = LocalSession.current.optBoolean("authenticated")
    val card = CatalogTitle(media)
    val (action, run) = rememberAction()
    val kind = coreData(CoreCommand.MediaKind, json("mediaType" to card.type))
    val context = androidx.compose.ui.platform.LocalContext.current
    val startContent: () -> Unit = {
        run {
            when (card.type) {
                "anime", "cinema" -> {
                    val preferences = context.getSharedPreferences("playback", android.content.Context.MODE_PRIVATE)
                    val key = "last:${card.type}:${card.id}"
                    val legacy = preferences.getString(key, null)?.let { runCatching { JSONObject(it) }.getOrNull() }
                    val episode = core.call(CoreCommand.ContinueEpisode, json("mediaId" to card.id, "legacy" to legacy)).optJSONObject("episode")
                    preferences.edit().remove(key).apply()
                    if (episode == null) action.message = ui(R.string.no_episodes)
                    else navigator.open(Screen.Player, json("url" to episode.text("url"), "episodeId" to episode.text("id"),
                        "episodeType" to episode.text("type"), "mediaId" to card.id, "mediaType" to card.type))
                }
                "manga" -> {
                    val preferences = context.getSharedPreferences("reader", android.content.Context.MODE_PRIVATE)
                    val key = "chapter:${card.slug}"
                    val selected = core.call(CoreCommand.ContinueChapter, json("mediaId" to card.id, "slug" to card.slug,
                        "legacyChapter" to preferences.getString(key, null)?.toDoubleOrNull()))
                    val chapters = selected.getJSONArray("chapters")
                    val chapter = if (selected.isNull("chapter")) null else selected.getDouble("chapter")
                    preferences.edit().remove(key).apply()
                    if (chapter == null) action.message = ui(R.string.no_chapters)
                    else navigator.open(Screen.Reader, json("slug" to card.slug, "chapter" to chapter, "chapters" to chapters))
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
                    Text(media.optJSONArray("genres").strings().joinToString(" Â· ", transform = ::keyLabel),
                        style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Text(keyLabel(card.type), style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.primary)
                    val production = listOfNotNull(media.text("studio"), media.text("country")?.let(::keyLabel)).joinToString(" Â· ")
                    if (production.isNotBlank()) Text(production, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
            Row(Modifier.fillMaxWidth().padding(vertical = 8.dp), horizontalArrangement = Arrangement.SpaceEvenly,
                verticalAlignment = Alignment.CenterVertically) {
                val facts = listOfNotNull(media.text("year"),
                    media.optDouble("rating").takeIf { it.isFinite() && it > 0 }?.let { "â˜… %.1f / 5".format(it / 2) },
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
                    favorite = { run { core.value(CoreCommand.SetFavorite, json("mediaId" to card.id, "mediaType" to card.type,
                        "selected" to !media.optBoolean("favorite"))); refresh() } },
                    progress = { status -> run { core.value(CoreCommand.SetMark, json("mediaId" to card.id, "mediaType" to card.type,
                        "status" to status, "current" to media.optJSONObject("mark")?.text("status"))); refresh() } },
                    rate = { rating -> run { core.value(CoreCommand.SetRating, json("mediaId" to card.id, "mediaType" to card.type,
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
                favorite = { run { core.value(CoreCommand.SetFavorite, json("mediaId" to card.id, "mediaType" to card.type,
                    "selected" to !media.optBoolean("favorite"))); refresh() } },
                progress = { status -> run { core.value(CoreCommand.SetMark, json("mediaId" to card.id, "mediaType" to card.type,
                    "status" to status, "current" to media.optJSONObject("mark")?.text("status"))); refresh() } },
                rate = { rating -> run { core.value(CoreCommand.SetRating, json("mediaId" to card.id, "mediaType" to card.type,
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
        val credits = listOfNotNull(media.text("studio"), media.text("country")?.let(::keyLabel)).joinToString(" Â· ")
        if (isTv && credits.isNotBlank()) Text(credits, style = MaterialTheme.typography.labelLarge)

    }
}
