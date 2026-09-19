package net.anibel.app

import android.net.Uri
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.runtime.*
import androidx.compose.material3.MaterialTheme
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import org.json.JSONObject

internal fun episodeVideoId(url: String?): String? {
    val uri = url?.let(Uri::parse) ?: return null
    if (uri.scheme != "https" || uri.host !in setOf("video.anibel.net", "video.anibel.stream")) return null
    return uri.pathSegments.firstOrNull()?.takeIf { it.matches(Regex("[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}")) }
}

@Composable
internal fun episodeScreenshots(url: String?): List<String>? {
    val id = episodeVideoId(url) ?: return emptyList()
    val core = LocalCore.current
    val cache = (androidx.compose.ui.platform.LocalContext.current.applicationContext as AnibelApplication).episodeArtwork
    return key(id) {
        produceState<List<String>?>(cache.peek(id), id) {
            value = try { cache.request(id, core).await() }
            catch (cancelled: kotlinx.coroutines.CancellationException) { throw cancelled }
            catch (_: Exception) { emptyList() }
        }.value
    }
}

internal fun videoScreenshots(info: JSONObject): List<String> = info.optJSONArray("screenshots").strings().mapNotNull { path ->
    runCatching {
        val uri = java.net.URI(path)
        val resolved = if (uri.isAbsolute) uri else java.net.URI(info.getString("host")).resolve(uri)
        resolved.toString().takeIf { resolved.scheme == "https" && resolved.host != null }
    }.getOrNull()
}

@Composable
internal fun EpisodePreview(mediaId: String, isTv: Boolean) {
    val choices = coreData("episodeChoices", json("mediaId" to mediaId, "kind" to "dub"))
    val episodes = ((choices as? RemoteData.Ready)?.value as? JSONObject)?.optJSONArray("items").objects()
    val previews = episodes.take(3).map { episode ->
        key(episode.optString("url")) { episodeScreenshots(episode.text("url")) }
    }
    val screenshots = previews.flatMap { it.orEmpty().take(2) }.distinct()
    val loading = choices == RemoteData.Loading || previews.any { it == null }
    val native = episodes.take(3).any { episodeVideoId(it.text("url")) != null }
    if (screenshots.isNotEmpty() || (!isTv && (loading || native))) {
        LazyRow(Modifier.testTag("episode_previews"), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            if (screenshots.isEmpty()) item {
                androidx.compose.material3.Surface(
                    Modifier.width(280.dp).aspectRatio(16f / 9f), shape = MaterialTheme.shapes.large,
                    color = MaterialTheme.colorScheme.surfaceContainerHigh) {
                    Box(contentAlignment = androidx.compose.ui.Alignment.Center) {
                        if (loading) LoadingSpinner()
                        else androidx.compose.material3.Icon(androidx.compose.ui.res.painterResource(R.drawable.ic_play), null)
                    }
                }
            }
            items(screenshots) { screenshot ->
                AppImage(screenshot, null, Modifier.width(280.dp).aspectRatio(16f / 9f).clip(MaterialTheme.shapes.large), contentScale = ContentScale.Crop)
            }
        }
    }
}
