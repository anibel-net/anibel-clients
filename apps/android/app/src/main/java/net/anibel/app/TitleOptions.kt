package net.anibel.app

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Intent
import android.net.Uri
import android.widget.Toast
import androidx.compose.foundation.layout.*
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.painterResource
import org.json.JSONObject

@Composable
internal fun TitleOptions(args: JSONObject, media: JSONObject?, isTv: Boolean = false) {
    val context = LocalContext.current
    var expanded by remember { mutableStateOf(false) }
    val slug = media?.text("slug") ?: args.text("slug")
    val type = media?.text("mediaType") ?: args.text("mediaType")
    val link = if (slug != null && type in AppPage.catalogs.map { it.mediaType })
        Uri.Builder().scheme("https").authority("anibel.net").appendPath(type).appendPath(slug).build() else null
    val title = media?.let { CatalogTitle(it).title } ?: ui(R.string.app_name)
    fun launch(intent: Intent) {
        try { context.startActivity(intent) }
        catch (_: android.content.ActivityNotFoundException) {
            Toast.makeText(context, ui(R.string.action_failed), Toast.LENGTH_SHORT).show()
        }
    }
    if (isTv) {
        androidx.tv.material3.IconButton({ expanded = true }, Modifier.testTag("title_options")) {
            androidx.tv.material3.Icon(painterResource(R.drawable.ic_more), ui(R.string.more_options))
        }
        if (expanded) Dialog(onDismissRequest = { expanded = false }) {
            val focus = remember { FocusRequester() }
            androidx.tv.material3.Surface(shape = androidx.tv.material3.MaterialTheme.shapes.large) {
                Column(Modifier.width(320.dp).padding(24.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    AppButton(ui(R.string.share_title), true, {
                        expanded = false
                        launch(Intent.createChooser(Intent(Intent.ACTION_SEND).apply {
                            this.type = "text/plain"; putExtra(Intent.EXTRA_TEXT, link.toString()); putExtra(Intent.EXTRA_SUBJECT, title)
                        }, null))
                    }, Modifier.fillMaxWidth().focusRequester(focus), enabled = link != null)
                    AppButton(ui(R.string.copy_link), true, {
                        expanded = false
                        context.getSystemService(ClipboardManager::class.java).setPrimaryClip(ClipData.newPlainText(title, link.toString()))
                    }, Modifier.fillMaxWidth(), enabled = link != null)
                    AppButton(ui(R.string.open_browser), true, { expanded = false; launch(Intent(Intent.ACTION_VIEW, link)) }, Modifier.fillMaxWidth(), enabled = link != null)
                    AppButton(ui(R.string.cancel), true, { expanded = false })
                }
            }
            LaunchedEffect(Unit) { if (link != null) focus.requestFocus() }
        }
        return
    }
    Box {
        IconButton({ expanded = true }, Modifier.testTag("title_options")) {
            Icon(painterResource(R.drawable.ic_more), ui(R.string.more_options))
        }
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            DropdownMenuItem(text = { Text(ui(R.string.share_title)) }, enabled = link != null, onClick = {
                expanded = false
                launch(Intent.createChooser(Intent(Intent.ACTION_SEND).apply {
                    this.type = "text/plain"
                    putExtra(Intent.EXTRA_TEXT, link.toString())
                    putExtra(Intent.EXTRA_SUBJECT, title)
                }, null))
            })
            DropdownMenuItem(text = { Text(ui(R.string.copy_link)) }, enabled = link != null, onClick = {
                expanded = false
                context.getSystemService(ClipboardManager::class.java).setPrimaryClip(ClipData.newPlainText(title, link.toString()))
                if (android.os.Build.VERSION.SDK_INT < 33)
                    Toast.makeText(context, ui(R.string.link_copied), Toast.LENGTH_SHORT).show()
            })
            DropdownMenuItem(text = { Text(ui(R.string.open_browser)) }, enabled = link != null, onClick = {
                expanded = false
                launch(Intent(Intent.ACTION_VIEW, link))
            })
        }
    }
}
