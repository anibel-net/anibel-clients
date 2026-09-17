package net.anibel.app

import androidx.compose.foundation.Image
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource

internal val AnibelBackground = Color(0xFF17131F)
internal val AnibelAccent = Color(0xFFD4BFFF)
internal val AnibelText = Color(0xFFF6F0FF)

@Composable
internal fun BrandMark(modifier: Modifier = Modifier) {
    Image(
        painter = painterResource(R.drawable.anibel_logo),
        contentDescription = null,
        modifier = modifier,
    )
}
