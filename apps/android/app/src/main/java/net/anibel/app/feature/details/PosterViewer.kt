package net.anibel.app

import android.graphics.Bitmap
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import coil3.SingletonImageLoader
import coil3.request.ImageRequest
import coil3.request.SuccessResult
import coil3.request.allowHardware
import coil3.size.Size
import coil3.toBitmap
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun PosterViewer(poster: String, title: String, id: String, close: () -> Unit) {
    val context = LocalContext.current
    val (action, run) = rememberAction()
    val save = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("image/png")) { uri ->
        if (uri != null) run {
            val result = SingletonImageLoader.get(context).execute(
                ImageRequest.Builder(context).data(poster).size(Size.ORIGINAL).allowHardware(false).build())
            val image = (result as? SuccessResult)?.image ?: error(ui(R.string.action_failed))
            withContext(Dispatchers.IO) {
                requireNotNull(context.contentResolver.openOutputStream(uri, "wt")).use { output ->
                    check(image.toBitmap().compress(Bitmap.CompressFormat.PNG, 100, output))
                }
            }
            action.message = ui(R.string.image_saved)
        }
    }
    Dialog(onDismissRequest = { if (!action.busy) close() }, properties = DialogProperties(
        usePlatformDefaultWidth = false, decorFitsSystemWindows = false, dismissOnBackPress = !action.busy)) {
        Scaffold(Modifier.fillMaxSize().testTag("poster_viewer"), topBar = {
            TopAppBar(title = { Text(title, maxLines = 1, overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis) },
                navigationIcon = {
                    IconButton(close, enabled = !action.busy) { Icon(painterResource(R.drawable.ic_close), ui(R.string.back)) }
                }, actions = {
                    IconButton({ save.launch("$id.png") }, enabled = !action.busy, modifier = Modifier.testTag("save_poster")) {
                        Icon(painterResource(R.drawable.ic_download), ui(R.string.download))
                    }
                })
        }) { padding ->
            Column(Modifier.fillMaxSize().padding(padding)) {
                ActionStatus(action, false)
                AppImage(poster, title, Modifier.fillMaxWidth().weight(1f), contentScale = ContentScale.Fit)
            }
        }
    }
}
