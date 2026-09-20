package net.anibel.app

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.relocation.BringIntoViewRequester
import androidx.compose.foundation.relocation.bringIntoViewRequester
import kotlinx.coroutines.launch
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import org.json.JSONObject

@Composable
internal fun CommentsScreen(media: JSONObject, isTv: Boolean) {
    var revision by remember { mutableIntStateOf(0) }
    var offset by rememberSaveable { mutableLongStateOf(0) }
    var draft by rememberSaveable { mutableStateOf("") }
    var reply by remember { mutableStateOf<JSONObject?>(null) }
    val core = LocalCore.current
    val (action, run) = rememberAction()
    val args = json("mediaId" to media.text("mediaId"), "mediaType" to media.text("mediaType"))
    val comments = coreData(CoreCommand.Comments, JSONObject(args.toString()).put("offset", offset).put("limit", 20), revision)
    val composer = remember { BringIntoViewRequester() }
    val scope = rememberCoroutineScope()
    Column(Modifier.fillMaxWidth().padding(horizontal = 24.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
        Column(Modifier.bringIntoViewRequester(composer), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        if (LocalSession.current.optBoolean("authenticated")) {
            reply?.let { target ->
                AppText(ui(R.string.reply_to, target.optJSONObject("user")?.optString("username").orEmpty()), isTv)
                AppButton(ui(R.string.cancel_reply), isTv, { reply = null }, enabled = !action.busy)
            }
            OutlinedTextField(draft, { draft = it }, Modifier.fillMaxWidth(), placeholder = { Text(ui(R.string.comment)) }, shape = MaterialTheme.shapes.large, minLines = 2, maxLines = 6, enabled = !action.busy)
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.End) {
            AppButton(ui(R.string.send), isTv, { run {
                val request = JSONObject(args.toString()).put("content", draft.trim())
                reply?.text("id")?.let { request.put("replyTo", it) }
                core.value(CoreCommand.AddComment, request)
                draft = ""; reply = null; offset = 0; revision++
            } }, enabled = !action.busy && draft.isNotBlank())
            }
        } else AppText(ui(R.string.sign_in_comment), isTv)
        ActionStatus(action, isTv)
        }
        DataContent(comments, isTv, { revision++ }) { value ->
            val page = value as? JSONObject ?: JSONObject()
            Column {
                val rows = page.optJSONArray("docs").objects()
                if (rows.isEmpty()) Text(ui(R.string.empty_comments), style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant)
                DetailItems(rows, isTv, dividers = false) { comment -> CommentItem(comment, isTv, 0) {
                    if (!action.busy) { reply = it; scope.launch { composer.bringIntoView() } }
                } }
                Row {
                    if (offset > 0) AppButton(ui(R.string.previous), isTv, { offset = (offset - 20).coerceAtLeast(0) })
                    if (offset + 20 < page.optLong("totalDocs")) AppButton(ui(R.string.next), isTv, { offset += 20 })
                }
            }
        }
    }
}

/** Mobile items share the page scroll; only visible batches are composed. */
