package net.anibel.app

import androidx.annotation.DrawableRes
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource

@Composable
internal fun SettingsGroup(content: @Composable ColumnScope.() -> Unit) {
    Card(Modifier.fillMaxWidth(),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainerLow),
        content = content)
}

@Composable
internal fun SettingsRow(title: String, supporting: String? = null, @DrawableRes icon: Int? = null,
    enabled: Boolean = true, onClick: (() -> Unit)? = null, trailing: @Composable (() -> Unit)? = null) {
    ListItem(
        headlineContent = { Text(title) },
        supportingContent = supporting?.let { { Text(it) } },
        leadingContent = icon?.let { { Icon(painterResource(it), null) } },
        trailingContent = trailing,
        modifier = if (onClick == null) Modifier else Modifier.clickable(enabled = enabled, onClick = onClick),
        colors = ListItemDefaults.colors(containerColor = Color.Transparent),
    )
}
