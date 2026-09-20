package net.anibel.app

import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.res.painterResource
import org.json.JSONObject

@Composable
internal fun MobileEpisodeCard(episode: JSONObject, screenshot: String?, screenshotLoading: Boolean, busy: Boolean, signedIn: Boolean,
    play: () -> Unit, download: () -> Unit, watched: () -> Unit) {
    var menu by remember { mutableStateOf(false) }
    val label = ui(R.string.episode_number, episode.optString("episode").removeSuffix(".0"))
    Card(onClick = play, modifier = Modifier.fillMaxWidth()) {
        if (episodeVideoId(episode.text("url")) != null) {
            Surface(color = MaterialTheme.colorScheme.surfaceContainerHigh) {
                Box(Modifier.fillMaxWidth().aspectRatio(16f / 9f), contentAlignment = androidx.compose.ui.Alignment.Center) {
                    if (screenshotLoading) LoadingSpinner()
                    else if (screenshot != null) AppImage(screenshot, null, Modifier.fillMaxSize(), contentScale = ContentScale.Crop)
                    else Icon(painterResource(R.drawable.ic_play), null)
                }
            }
        }
        ListItem(
            headlineContent = { Text(episode.text("title") ?: label, style = MaterialTheme.typography.titleMedium) },
            supportingContent = {
                val details = listOfNotNull(label.takeIf { episode.text("title") != null },
                    ui(R.string.watched_checked).takeIf { episode.optBoolean("watched") })
                if (details.isNotEmpty()) Text(details.joinToString(" · "))
            },
            leadingContent = { Icon(painterResource(R.drawable.ic_play), ui(R.string.play)) },
            trailingContent = {
                Box {
                    IconButton({ menu = true }) { Icon(painterResource(R.drawable.ic_more), ui(R.string.more_options)) }
                    DropdownMenu(menu, { menu = false }) {
                        DropdownMenuItem(text = { Text(ui(R.string.download)) }, enabled = !busy,
                            leadingIcon = { Icon(painterResource(R.drawable.ic_download), null) },
                            onClick = { menu = false; download() })
                        if (signedIn) DropdownMenuItem(
                            text = { Text(ui(if (episode.optBoolean("watched")) R.string.watched_checked else R.string.mark_watched)) },
                            enabled = !busy, onClick = { menu = false; watched() })
                    }
                }
            }, colors = ListItemDefaults.colors(containerColor = CardDefaults.cardColors().containerColor))
    }
}
