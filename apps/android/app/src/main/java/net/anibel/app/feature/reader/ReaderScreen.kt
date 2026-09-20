package net.anibel.app

import androidx.compose.foundation.gestures.detectTransformGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.Alignment
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.unit.dp
import coil3.compose.AsyncImage
import org.json.JSONObject

@Composable
internal fun ReaderScreen(args: JSONObject, isTv: Boolean, onBack: () -> Unit = {}) {
    if (!isTv) {
        MobileReader(args, onBack)
        return
    }
    var chapter by rememberSaveable { mutableDoubleStateOf(args.getDouble("chapter")) }
    var revision by remember { mutableIntStateOf(0) }
    var singlePage by rememberSaveable { mutableStateOf(isTv) }
    var index by rememberSaveable(chapter) { mutableIntStateOf(0) }
    var zoom by rememberSaveable(chapter, index) { mutableFloatStateOf(1f) }
    var pan by remember(chapter, index) { mutableStateOf(Offset.Zero) }
    val data = coreData(CoreCommand.ReaderOpen, json("slug" to args.getString("slug"), "chapter" to chapter, "chapters" to args.optJSONArray("chapters")), revision)
    Column(Modifier.fillMaxSize()) {
        DataContent(data, isTv, { revision++ }, loadingModifier = Modifier.fillMaxSize()) { value ->
            val result = value as? JSONObject ?: JSONObject()
            val images = result.optJSONObject("chapter")?.optJSONArray("images").objects()
            if (!isTv) Row(Modifier.fillMaxWidth().padding(horizontal = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                IconButton({ chapter = result.getDouble("previous") }, enabled = !result.isNull("previous")) {
                    Icon(painterResource(R.drawable.ic_back), ui(R.string.previous_chapter))
                }
                Text(ui(R.string.chapter_number, chapter.toString().removeSuffix(".0")), Modifier.weight(1f), style = MaterialTheme.typography.labelLarge)
                IconButton({ chapter = result.getDouble("next") }, enabled = !result.isNull("next")) {
                    Icon(painterResource(R.drawable.ic_back), ui(R.string.next_chapter), Modifier.rotate(180f))
                }
                TextButton({ singlePage = !singlePage; zoom = 1f; pan = Offset.Zero }) {
                    Text(ui(if (singlePage) R.string.scroll_pages else R.string.single_page))
                }
            } else {
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                AppButton(ui(R.string.previous_chapter), isTv, { chapter = result.getDouble("previous") }, enabled = !result.isNull("previous"))
                AppButton(ui(R.string.next_chapter), isTv, { chapter = result.getDouble("next") }, enabled = !result.isNull("next"))
            }
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                AppButton(if (singlePage) ui(R.string.scroll_pages) else ui(R.string.single_page), isTv, { singlePage = !singlePage; zoom = 1f })
                if (singlePage) AppButton("−", isTv, { zoom = (zoom - .25f).coerceAtLeast(1f) })
                if (singlePage) AppButton("+", isTv, { zoom = (zoom + .25f).coerceAtMost(3f) })
            }
            }
            if (images.isEmpty()) AppText(ui(R.string.no_pages), isTv)
            else if (singlePage) {
                Row {
                    AppButton(ui(R.string.previous_page), isTv, { index-- }, enabled = index > 0)
                    AppText("${index + 1} / ${images.size}", isTv, Modifier.padding(12.dp))
                    AppButton(ui(R.string.next_page), isTv, { index++ }, enabled = index + 1 < images.size)
                }
                Box(Modifier.fillMaxWidth().weight(1f).clipToBounds()) {
                    ReaderPage(images[index.coerceIn(images.indices)].text("large"), index, isTv,
                        Modifier.fillMaxSize().pointerInput(chapter, index) {
                            detectTransformGestures { _, movement, scale, _ ->
                                zoom = (zoom * scale).coerceIn(1f, 3f)
                                val limitX = size.width * (zoom - 1f) / 2f
                                val limitY = size.height * (zoom - 1f) / 2f
                                pan = Offset((pan.x + movement.x).coerceIn(-limitX, limitX), (pan.y + movement.y).coerceIn(-limitY, limitY))
                            }
                        }.graphicsLayer(scaleX = zoom, scaleY = zoom, translationX = pan.x, translationY = pan.y), true)
                }
            } else key(chapter) { LazyColumn(Modifier.weight(1f).testTag("reader_pages")) {
                itemsIndexed(images) { page, image ->
                    ReaderPage(image.text("large"), page, isTv, Modifier.fillMaxWidth())
                }
            } }
        }
    }
}


@Composable
internal fun ReaderPage(url: String?, page: Int, isTv: Boolean, modifier: Modifier, fit: Boolean = false) {
    var revision by remember(url) { mutableIntStateOf(0) }
    var ratio by remember(url) { mutableFloatStateOf(2f / 3f) }
    var state by remember(url, revision) { mutableStateOf(LoadState.Loading) }
    Box(modifier.then(if (fit) Modifier else Modifier.aspectRatio(ratio)), contentAlignment = Alignment.Center) {
        key(url, revision) {
            AsyncImage(url, ui(R.string.page_number, page + 1), Modifier.fillMaxSize()
                .testTag(if (state == LoadState.Ready) "reader_page_ready" else "reader_page_loading"),
                contentScale = if (fit) ContentScale.Fit else ContentScale.FillWidth,
                onSuccess = { result ->
                    val image = result.result.image
                    if (image.width > 0 && image.height > 0) ratio = image.width.toFloat() / image.height
                    state = LoadState.Ready
                }, onError = { state = LoadState.Failed })
        }
        when (state) {
            LoadState.Loading -> LoadingSpinner(Modifier.size(32.dp))
            LoadState.Failed -> Column(horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(12.dp), modifier = Modifier.testTag("reader_page_error")) {
                AppText(ui(R.string.page_load_failed, page + 1), isTv)
                AppButton(ui(R.string.try_again), isTv, { revision++ })
            }
            LoadState.Ready -> Unit
        }
    }
}
