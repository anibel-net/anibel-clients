package net.anibel.app

import android.app.Activity
import android.content.Context
import android.content.ContextWrapper
import android.content.res.Configuration
import androidx.annotation.StringRes

internal enum class AppTheme(@get:StringRes val labelId: Int) {
    System(R.string.theme_system), Light(R.string.theme_light), Dark(R.string.theme_dark)
}

internal object Appearance {
    fun current(context: Context): AppTheme {
        val saved = context.getSharedPreferences("preferences", Context.MODE_PRIVATE).getString("theme", null)
        return AppTheme.entries.firstOrNull { it.name == saved } ?: AppTheme.System
    }

    fun wrap(context: Context): Context {
        val night = when (current(context)) {
            AppTheme.System -> return context
            AppTheme.Light -> Configuration.UI_MODE_NIGHT_NO
            AppTheme.Dark -> Configuration.UI_MODE_NIGHT_YES
        }
        val configuration = Configuration(context.resources.configuration)
        configuration.uiMode = configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK.inv() or night
        return context.createConfigurationContext(configuration)
    }

    fun select(context: Context, theme: AppTheme) {
        if (current(context) == theme) return
        context.getSharedPreferences("preferences", Context.MODE_PRIVATE).edit().putString("theme", theme.name).apply()
        var owner = context
        while (owner is ContextWrapper && owner !is Activity) owner = owner.baseContext
        (owner as? Activity)?.recreate()
    }
}
