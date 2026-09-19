package net.anibel.app

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.unit.dp
import org.json.JSONObject

@Composable
internal fun ProfileCover(user: JSONObject, edgeToEdge: Boolean, isTv: Boolean = false) {
    val surface = MaterialTheme.colorScheme.surface
    Column(Modifier.fillMaxWidth()) {
        Box(Modifier.fillMaxWidth().height(if (isTv) 160.dp else 232.dp).background(MaterialTheme.colorScheme.surfaceContainerHigh)) {
            AppImage(user.text("wallpaper"), null, Modifier.matchParentSize(), contentScale = ContentScale.Crop)
            Box(Modifier.matchParentSize().background(Brush.verticalGradient(listOf(surface.copy(alpha = if (edgeToEdge) 0.75f else 0.3f), surface.copy(alpha = 0.2f), surface))))
            AppImage(user.text("avatar"), null, Modifier.align(Alignment.BottomStart).padding(start = 24.dp).size(if (isTv) 64.dp else 88.dp)
                .clip(CircleShape).background(surface), contentScale = ContentScale.Crop,
                fallback = painterResource(R.drawable.ic_profile), error = painterResource(R.drawable.ic_profile))
        }
        Column(Modifier.padding(start = 24.dp, end = 24.dp, top = 16.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text(user.text("displayName") ?: user.optString("username"), style = MaterialTheme.typography.headlineSmall)
            Text("@${user.optString("username")}", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            user.text("bio")?.takeIf { it.isNotBlank() }?.let {
                Text(it, Modifier.padding(top = 8.dp), style = MaterialTheme.typography.bodyMedium)
            }
        }
    }
}
