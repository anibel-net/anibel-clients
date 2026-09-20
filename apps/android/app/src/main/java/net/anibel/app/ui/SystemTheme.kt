package net.anibel.app

import android.os.Build
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.dynamicDarkColorScheme
import androidx.compose.material3.dynamicLightColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.platform.LocalContext
import androidx.compose.material3.MaterialExpressiveTheme as MobileTheme
import androidx.tv.material3.MaterialTheme as TvTheme
import androidx.tv.material3.LocalContentColor as TvContentColor

@OptIn(androidx.compose.material3.ExperimentalMaterial3ExpressiveApi::class)
@Composable
internal fun SystemTheme(isTv: Boolean = false, content: @Composable () -> Unit) {
    val dark = isSystemInDarkTheme()
    val context = LocalContext.current
    val colors = when {
        Build.VERSION.SDK_INT >= Build.VERSION_CODES.S ->
            if (dark) dynamicDarkColorScheme(context) else dynamicLightColorScheme(context)
        dark -> darkColorScheme()
        else -> lightColorScheme()
    }
    if (isTv) {
        // TV Material uses its own color type; keep the platform's color roles.
        MobileTheme(colorScheme = colors) {
        TvTheme(
            colorScheme = androidx.tv.material3.lightColorScheme(
                primary = colors.primary,
                onPrimary = colors.onPrimary,
                primaryContainer = colors.primaryContainer,
                onPrimaryContainer = colors.onPrimaryContainer,
                inversePrimary = colors.inversePrimary,
                secondary = colors.secondary,
                onSecondary = colors.onSecondary,
                secondaryContainer = colors.secondaryContainer,
                onSecondaryContainer = colors.onSecondaryContainer,
                tertiary = colors.tertiary,
                onTertiary = colors.onTertiary,
                tertiaryContainer = colors.tertiaryContainer,
                onTertiaryContainer = colors.onTertiaryContainer,
                background = colors.background,
                onBackground = colors.onBackground,
                surface = colors.surface,
                onSurface = colors.onSurface,
                surfaceVariant = colors.surfaceVariant,
                onSurfaceVariant = colors.onSurfaceVariant,
                surfaceTint = colors.surfaceTint,
                inverseSurface = colors.inverseSurface,
                inverseOnSurface = colors.inverseOnSurface,
                error = colors.error,
                onError = colors.onError,
                errorContainer = colors.errorContainer,
                onErrorContainer = colors.onErrorContainer,
                border = colors.outline,
                borderVariant = colors.outlineVariant,
                scrim = colors.scrim,
            ),
        ) {
            CompositionLocalProvider(TvContentColor provides colors.onSurface, androidx.compose.material3.LocalContentColor provides colors.onSurface, content = content)
        }
        }
    } else {
        MobileTheme(colorScheme = colors) {
            CompositionLocalProvider(androidx.compose.material3.LocalContentColor provides colors.onSurface, content = content)
        }
    }
}
