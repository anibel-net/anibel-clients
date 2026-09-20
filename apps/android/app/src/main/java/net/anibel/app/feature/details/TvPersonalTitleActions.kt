package net.anibel.app

import androidx.compose.foundation.layout.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.tv.material3.*

private enum class TvPersonalEditor { Progress, Rating }

@OptIn(ExperimentalTvMaterial3Api::class)
@Composable
internal fun TvPersonalTitleActions(selected: Boolean, status: String, rating: Double, marks: List<String>, busy: Boolean,
    favorite: () -> Unit, progress: (String) -> Unit, rate: (Int) -> Unit) {
    var editor by rememberSaveable { mutableStateOf<TvPersonalEditor?>(null) }
    Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        FilterChip(selected, favorite, enabled = !busy, modifier = Modifier.testTag("detail_favorite")) { Text(ui(R.string.favorites)) }
        AppButton(if (status == "notselected") ui(R.string.my_progress) else keyLabel(status), true,
            { editor = TvPersonalEditor.Progress }, Modifier.testTag("detail_progress"), enabled = !busy && marks.isNotEmpty())
        AppButton(if (rating > 0) ui(R.string.rating_value, rating) else ui(R.string.rate_title), true,
            { editor = TvPersonalEditor.Rating }, Modifier.testTag("detail_rating"), enabled = !busy)
    }
    editor?.let { active ->
        val focus = remember { FocusRequester() }
        Dialog(onDismissRequest = { editor = null }) {
            Surface(shape = MaterialTheme.shapes.large) {
                Column(Modifier.width(360.dp).padding(24.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    Text(ui(if (active == TvPersonalEditor.Progress) R.string.my_progress else R.string.rate_title), style = MaterialTheme.typography.titleLarge)
                    if (active == TvPersonalEditor.Progress) marks.forEachIndexed { index, mark ->
                        FilterChip(selected = mark == status, onClick = { editor = null; if (mark != status) progress(mark) },
                            modifier = Modifier.fillMaxWidth().then(if (index == 0) Modifier.focusRequester(focus) else Modifier)) { Text(keyLabel(mark)) }
                    } else (1..5).forEach { value ->
                        FilterChip(selected = value.toDouble() == rating, onClick = { editor = null; rate(value) },
                            modifier = Modifier.fillMaxWidth().testTag("rate_$value").then(if (value == 1) Modifier.focusRequester(focus) else Modifier)) {
                            Text("${"★".repeat(value)}  ${ui(R.string.rating_value, value.toDouble())}")
                        }
                    }
                    AppButton(ui(R.string.cancel), true, { editor = null })
                }
            }
            LaunchedEffect(active) { focus.requestFocus() }
        }
    }
}
