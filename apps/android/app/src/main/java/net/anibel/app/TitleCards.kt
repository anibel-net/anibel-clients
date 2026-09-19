package net.anibel.app

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.grid.*
import androidx.compose.ui.draw.clip
import androidx.compose.foundation.background
import androidx.compose.material3.Card
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.unit.dp
import org.json.JSONArray

@Composable
internal fun TitleCard(title: CatalogTitle, isTv: Boolean, modifier: Modifier = Modifier) {
    val navigator = LocalNavigator.current
    val click = { navigator.open(Screen.Detail, json("slug" to title.slug, "mediaType" to title.type)) }
    val content: @Composable () -> Unit = {
        Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Box {
            AppImage(title.poster, null, Modifier.fillMaxWidth().aspectRatio(2f / 3f).clip(MaterialTheme.shapes.medium).background(MaterialTheme.colorScheme.surfaceContainer),
                contentScale = ContentScale.Crop, error = painterResource(R.drawable.ic_catalogs),
                fallback = painterResource(R.drawable.ic_catalogs))
                val kinds = title.data.text("updateType")?.takeIf { it in listOf("dub", "sub") }?.let(::listOf)
                    ?: title.data.optJSONArray("language").strings().filter { it in listOf("dub", "sub") }.distinct()
                if (kinds.isNotEmpty()) Row(Modifier.align(androidx.compose.ui.Alignment.TopEnd).padding(6.dp),
                    horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                    kinds.forEach { kind ->
                        androidx.compose.material3.Surface(shape = MaterialTheme.shapes.small, color = MaterialTheme.colorScheme.surfaceContainerHigh) {
                            androidx.compose.material3.Icon(painterResource(if (kind == "dub") R.drawable.ic_microphone else R.drawable.ic_subtitles),
                                keyLabel(kind), Modifier.padding(4.dp).size(16.dp))
                        }
                    }
                }
            }
            Column(Modifier.padding(horizontal = 2.dp, vertical = 0.dp), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                if (isTv) androidx.tv.material3.Text(title.title, maxLines = 2,
                    style = androidx.tv.material3.MaterialTheme.typography.titleSmall,
                    overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis)
                else androidx.compose.material3.Text(title.title, maxLines = 2,
                    style = MaterialTheme.typography.bodyMedium, overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis)
                androidx.compose.material3.Text(if (isTv) title.yearAndRating else title.meta, maxLines = 1,
                    style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant,
                    overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis)
                if (title.info.isNotBlank() && (!isTv || title.data.text("num") != null)) androidx.compose.material3.Text(title.info, maxLines = 1,
                    style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                    overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis)
            }
        }
    }
    if (isTv) androidx.tv.material3.Surface(click, modifier.testTag("title_${title.id}"),
        colors = androidx.tv.material3.ClickableSurfaceDefaults.colors(containerColor = androidx.tv.material3.MaterialTheme.colorScheme.background)) {
        Box(Modifier.padding(4.dp)) { content() }
    }
    else Card(onClick = click, colors = androidx.compose.material3.CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.background), modifier = modifier.testTag("title_${title.id}")) { content() }

}

@Composable
internal fun TitleRow(values: JSONArray?, isTv: Boolean) {
    LazyRow(horizontalArrangement = Arrangement.spacedBy(16.dp), contentPadding = PaddingValues(8.dp)) {
        items(values.objects()) { value -> TitleCard(CatalogTitle(value), isTv, Modifier.width(if (isTv) 132.dp else 144.dp)) }
    }
}

@Composable
internal fun TitleGrid(values: JSONArray?, isTv: Boolean, modifier: Modifier = Modifier,
    contentPadding: PaddingValues = PaddingValues(24.dp), showCount: Boolean = false) {
    val cards = values.objects()
    LazyVerticalGrid(GridCells.Adaptive(if (isTv) 124.dp else 108.dp), modifier.fillMaxSize(),
        contentPadding = contentPadding, horizontalArrangement = Arrangement.spacedBy(16.dp),
        verticalArrangement = Arrangement.spacedBy(20.dp)) {
        if (showCount) item(span = { GridItemSpan(maxLineSpan) }) {
            androidx.compose.material3.Text(ui(R.string.titles_count, cards.size), style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        if (cards.isEmpty()) item(span = { GridItemSpan(maxLineSpan) }) { AppText(ui(R.string.empty_titles), isTv) }
        items(cards) { TitleCard(CatalogTitle(it), isTv) }
    }
}
