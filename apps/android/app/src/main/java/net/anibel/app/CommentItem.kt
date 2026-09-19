package net.anibel.app

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.unit.dp
import org.json.JSONObject

@Composable
internal fun CommentItem(comment: JSONObject, isTv: Boolean, depth: Int, reply: (JSONObject) -> Unit) {
    val navigator = LocalNavigator.current
    val context = LocalContext.current
    val user = comment.optJSONObject("user")
    val username = user?.text("username")
    val name = user?.text("displayName") ?: username ?: ui(R.string.user)
    val openAuthor = { username?.let { navigator.open(Screen.Profile, json("username" to it)) }; Unit }
    val created = comment.optLong("created")
    val date = remember(created, context) {
        if (created in 1..253402300799999L) android.text.format.DateUtils.formatDateTime(context, created,
            android.text.format.DateUtils.FORMAT_SHOW_DATE or android.text.format.DateUtils.FORMAT_ABBREV_MONTH or
                android.text.format.DateUtils.FORMAT_SHOW_TIME) else ""
    }
    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            AppImage(user?.text("avatar"), name, Modifier.size(if (depth == 0) 36.dp else 28.dp)
                .clip(CircleShape).clickable(enabled = username != null, onClick = openAuthor),
                contentScale = ContentScale.Crop, fallback = painterResource(R.drawable.ic_profile),
                error = painterResource(R.drawable.ic_profile))
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                if (isTv) AppButton(name, true, openAuthor, enabled = username != null)
                else Text(name, Modifier.clickable(enabled = username != null, onClick = openAuthor),
                    style = MaterialTheme.typography.labelLarge)
                if (date.isNotEmpty()) Text(date, style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant)
                SelectionContainer { Text(comment.optString("content"), style = MaterialTheme.typography.bodyMedium) }
                if (LocalSession.current.optBoolean("authenticated")) {
                    if (isTv) AppButton(ui(R.string.reply), true, { reply(comment) })
                    else TextButton({ reply(comment) }, contentPadding = PaddingValues(horizontal = 0.dp)) { Text(ui(R.string.reply)) }
                }
            }
        }
        val replies = comment.optJSONArray("replies").objects()
        var expanded by remember(comment.optString("id")) { mutableStateOf(false) }
        if (replies.isNotEmpty() && depth < 2) {
            Column(Modifier.padding(start = if (depth == 0) 48.dp else 0.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
                val label = ui(if (expanded) R.string.hide_replies else R.string.show_replies, replies.size)
                if (isTv) AppButton(label, true, { expanded = !expanded })
                else TextButton({ expanded = !expanded }, contentPadding = PaddingValues(horizontal = 0.dp)) { Text(label) }
                if (expanded) replies.forEach { CommentItem(it, isTv, depth + 1, reply) }
            }
        }
    }
}
