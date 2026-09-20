package net.anibel.app

import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.unit.dp

@Composable
internal fun SectionHeading(title: String, modifier: Modifier = Modifier, isTv: Boolean = false, open: () -> Unit) {
    Row(modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        Text(title, Modifier.weight(1f), style = MaterialTheme.typography.titleMedium)
        if (isTv) androidx.tv.material3.IconButton(open, Modifier.size(36.dp)) {
            androidx.tv.material3.Icon(painterResource(R.drawable.ic_back), title, Modifier.size(20.dp).rotate(180f))
        } else FilledTonalIconButton(open, Modifier.size(36.dp), colors = IconButtonDefaults.filledTonalIconButtonColors(containerColor = MaterialTheme.colorScheme.surfaceContainerHigh)) {
            Icon(painterResource(R.drawable.ic_back), title, Modifier.size(20.dp).rotate(180f))
        }
    }
}
