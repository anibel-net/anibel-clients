package net.anibel.app

import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.painter.Painter
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import coil3.compose.AsyncImagePainter
import coil3.compose.SubcomposeAsyncImage

@OptIn(ExperimentalMaterial3ExpressiveApi::class)
@Composable
internal fun LoadingSpinner(modifier: Modifier = Modifier) {
    LoadingIndicator(modifier.semantics { contentDescription = ui(R.string.loading) })
}

@Composable
internal fun LoadingContent(modifier: Modifier = Modifier) {
    Box(modifier.fillMaxWidth().padding(24.dp), contentAlignment = Alignment.Center) {
        LoadingSpinner()
    }
}

@Composable
internal fun ActionStatus(action: PageAction, isTv: Boolean) {
    if (action.busy) LinearProgressIndicator(Modifier.fillMaxWidth())
    if (action.message.isNotBlank()) AppText(action.message, isTv)
}

@Composable
internal fun AppImage(model: Any?, contentDescription: String?, modifier: Modifier = Modifier,
    contentScale: ContentScale = ContentScale.Fit, error: Painter? = null, fallback: Painter? = error,
    onError: ((AsyncImagePainter.State.Error) -> Unit)? = null) {
    SubcomposeAsyncImage(model, contentDescription, modifier, contentScale = contentScale,
        loading = { Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { LoadingSpinner(Modifier.size(32.dp)) } },
        error = {
            val painter = if (model == null) fallback else error
            if (painter != null) androidx.compose.foundation.Image(painter, null, Modifier.fillMaxSize(), contentScale = contentScale)
        }, onError = onError)
}
