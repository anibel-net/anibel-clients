package net.anibel.app

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import org.json.JSONObject

@Composable
internal fun DetailItems(rows: List<JSONObject>, isTv: Boolean, dividers: Boolean = true, grid: Boolean = false, content: @Composable (JSONObject) -> Unit) {
    if (isTv && grid) androidx.compose.foundation.lazy.grid.LazyVerticalGrid(
        androidx.compose.foundation.lazy.grid.GridCells.Adaptive(200.dp),
        contentPadding = PaddingValues(8.dp), horizontalArrangement = Arrangement.spacedBy(16.dp),
        verticalArrangement = Arrangement.spacedBy(20.dp)) {
        items(rows.size) { content(rows[it]) }
    } else if (isTv) LazyColumn(contentPadding = PaddingValues(8.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
        items(rows) { content(it) }
    } else {
        var visible by rememberSaveable(rows.size) { mutableIntStateOf(8) }
        Column(verticalArrangement = Arrangement.spacedBy(if (dividers) 8.dp else 16.dp)) {
            rows.take(visible).forEach { item ->
                Column(Modifier.fillMaxWidth()) { content(item) }
                if (dividers) HorizontalDivider()
            }
            if (visible < rows.size) OutlinedButton({ visible = (visible + 8).coerceAtMost(rows.size) }, Modifier.fillMaxWidth()) { Text(ui(R.string.load_more)) }
        }
    }
}
