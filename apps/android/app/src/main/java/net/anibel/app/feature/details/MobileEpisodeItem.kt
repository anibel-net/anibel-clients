package net.anibel.app

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.clickable
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import coil3.request.ImageRequest
import coil3.request.crossfade
import org.json.JSONObject

@Composable
internal fun MobileEpisodeItem(episode: JSONObject, screenshot: String?, screenshotLoading: Boolean, busy: Boolean, signedIn: Boolean,
    play: () -> Unit, download: () -> Unit, watched: () -> Unit) {
    var menu by remember { mutableStateOf(false) }
    val context = LocalContext.current
    val released = episode.optLong("released")
    val date = remember(released, context) {
        if (released in 1..253402300799999L) android.text.format.DateUtils.formatDateTime(context, released,
            android.text.format.DateUtils.FORMAT_SHOW_DATE or android.text.format.DateUtils.FORMAT_SHOW_YEAR or
                android.text.format.DateUtils.FORMAT_NUMERIC_DATE) else ""
    }
    val label = ui(R.string.episode_number, episode.optString("episode").removeSuffix(".0"))
    ListItem(
        modifier = Modifier.fillMaxWidth().clickable(onClick = play),
        headlineContent = { Text(episode.text("title") ?: label, maxLines = 2,
            overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis) },
        supportingContent = {
            val details = listOfNotNull(label.takeIf { episode.text("title") != null },
                date.takeIf(String::isNotBlank))
            if (details.isNotEmpty()) Text(details.joinToString(" · "))
        },
        leadingContent = {
            Box(Modifier.width(if (LocalConfiguration.current.screenWidthDp >= 600) 144.dp else 96.dp)
                .aspectRatio(16f / 9f).clip(MaterialTheme.shapes.small),
                contentAlignment = androidx.compose.ui.Alignment.Center) {
                if (screenshot != null) AppImage(
                    ImageRequest.Builder(LocalContext.current).data(screenshot).crossfade(true).build(),
                    null, Modifier.fillMaxSize(), contentScale = ContentScale.Crop)
                else if (screenshotLoading) LoadingSpinner(Modifier.size(24.dp))
                else Icon(painterResource(R.drawable.ic_play), ui(R.string.play))
            }
        },
        trailingContent = {
            Row(verticalAlignment = androidx.compose.ui.Alignment.CenterVertically) {
                Checkbox(checked = episode.optBoolean("watched"), onCheckedChange = { watched() }, enabled = signedIn && !busy,
                    modifier = Modifier.semantics { contentDescription = ui(R.string.mark_watched) })
                Box {
                    IconButton({ menu = true }) { Icon(painterResource(R.drawable.ic_more), ui(R.string.more_options)) }
                    DropdownMenu(menu, { menu = false }) {
                        DropdownMenuItem(text = { Text(ui(R.string.download)) }, enabled = !busy,
                            leadingIcon = { Icon(painterResource(R.drawable.ic_download), null) },
                            onClick = { menu = false; download() })
                    }
                }
            }
        })
}
