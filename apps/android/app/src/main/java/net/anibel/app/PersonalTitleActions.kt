package net.anibel.app

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.selection.selectable
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.unit.dp
import org.json.JSONObject

private enum class PersonalAction { Progress, Rating }

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun PersonalTitleActions(media: JSONObject, kind: JSONObject?, busy: Boolean,
    favorite: () -> Unit, progress: (String) -> Unit, rate: (Int) -> Unit, isTv: Boolean = false) {
    var editor by rememberSaveable { mutableStateOf<PersonalAction?>(null) }
    val status = media.optJSONObject("mark")?.text("status") ?: "notselected"
    val rating = media.optDouble("iRated", 0.0).takeIf { it.isFinite() }?.div(2)?.coerceIn(0.0, 5.0) ?: 0.0
    val marks = kind?.optJSONArray("marks").strings()
    if (isTv) {
        TvPersonalTitleActions(media.optBoolean("favorite"), status, rating, marks, busy, favorite, progress, rate)
        return
    }
    FlowRow(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        FilterChip(selected = media.optBoolean("favorite"), onClick = favorite, enabled = !busy,
            label = { Text(ui(R.string.favorites)) }, leadingIcon = {
                Icon(painterResource(R.drawable.ic_favorite), null, Modifier.size(18.dp))
            }, modifier = Modifier.testTag("detail_favorite"))
        AssistChip(onClick = { editor = PersonalAction.Progress }, enabled = !busy && marks.isNotEmpty(),
            label = { Text(if (status == "notselected") ui(R.string.my_progress) else keyLabel(status)) },
            leadingIcon = { Icon(painterResource(R.drawable.ic_catalogs), null, Modifier.size(18.dp)) },
            modifier = Modifier.testTag("detail_progress"))
        AssistChip(onClick = { editor = PersonalAction.Rating }, enabled = !busy,
            label = { Text(if (rating > 0) ui(R.string.rating_value, rating) else ui(R.string.rate_title)) },
            leadingIcon = { Icon(painterResource(R.drawable.ic_star), null, Modifier.size(18.dp)) },
            modifier = Modifier.testTag("detail_rating"))
    }
    when (editor) {
        PersonalAction.Progress -> ModalBottomSheet(onDismissRequest = { editor = null }) {
            Text(ui(R.string.my_progress), Modifier.padding(horizontal = 24.dp, vertical = 8.dp), style = MaterialTheme.typography.titleLarge)
            marks.forEach { mark ->
                ListItem(headlineContent = { Text(keyLabel(mark)) },
                    leadingContent = { RadioButton(selected = status == mark, onClick = null) },
                    modifier = Modifier.selectable(selected = status == mark, role = Role.RadioButton) {
                        editor = null
                        if (status != mark) progress(mark)
                    })
            }
            Spacer(Modifier.height(16.dp))
        }
        PersonalAction.Rating -> AlertDialog(onDismissRequest = { editor = null },
            title = { Text(ui(R.string.rate_title)) },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    Text(ui(R.string.rating_hint))
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceEvenly) {
                        (1..5).forEach { value ->
                            IconButton(onClick = { editor = null; rate(value) }, modifier = Modifier.testTag("rate_$value")) {
                                Icon(painterResource(if (value <= rating) R.drawable.ic_star else R.drawable.ic_star_outline),
                                    ui(R.string.rating_value, value.toDouble()), tint = MaterialTheme.colorScheme.primary)
                            }
                        }
                    }
                }
            }, confirmButton = {}, dismissButton = { TextButton({ editor = null }) { Text(ui(R.string.cancel)) } })
        null -> Unit
    }
}
