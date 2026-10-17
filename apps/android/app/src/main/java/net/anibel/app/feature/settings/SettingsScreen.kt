package net.anibel.app

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.clickable
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import org.json.JSONObject

@Composable
internal fun SettingsScreen(isTv: Boolean) {
    val core = LocalCore.current
    val context = LocalContext.current
    val (action, run) = rememberAction()
    var revision by remember { mutableIntStateOf(0) }
    val version = coreData(CoreCommand.Version, revision = revision)
    if (!isTv) {
        PageColumn(onRefresh = { revision++ }, refreshing = revision > 0 && version == RemoteData.Loading) {
            Text(ui(R.string.app_language), style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.primary)
            SettingsGroup {
                AppLanguage.entries.forEach { language ->
                    SettingsRow(language.label,
                        trailing = { RadioButton(Language.current(context) == language, onClick = null) },
                        onClick = {
                            var owner = context
                            while (owner is android.content.ContextWrapper && owner !is android.app.Activity) owner = owner.baseContext
                            (owner as? android.app.Activity)?.let { Language.select(it, language) }
                        })
                }
            }
            Text(ui(R.string.appearance), style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.primary)
            SettingsGroup {
                AppTheme.entries.forEach { theme ->
                    SettingsRow(ui(theme.labelId),
                        trailing = { RadioButton(Appearance.current(context) == theme, onClick = null) },
                        onClick = { Appearance.select(context, theme) })
                }
            }
            Text(ui(R.string.storage), style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.primary)
            SettingsGroup {
                SettingsRow(ui(R.string.clear_catalog_cache), icon = R.drawable.ic_refresh, enabled = !action.busy, onClick = {
                    run { core.value(CoreCommand.ClearCache); action.message = ui(R.string.catalog_cache_cleared) }
                })
                HorizontalDivider()
                SettingsRow(ui(R.string.clear_playback_files), ui(R.string.downloads_kept), R.drawable.ic_download,
                    enabled = !action.busy, onClick = {
                        run { core.value(CoreCommand.ClearPlaybackAssets); action.message = ui(R.string.playback_files_cleared) }
                    })
            }
            ActionStatus(action, false)
            SettingsGroup {
                SettingsRow(ui(R.string.about_app), ui(R.string.app_version, BuildConfig.VERSION_NAME), R.drawable.ic_more)
                DataContent(version, false, { revision++ }) {
                    Text(ui(R.string.core_version, (it as? JSONObject)?.optString("version").orEmpty()),
                        Modifier.padding(horizontal = 16.dp, vertical = 12.dp),
                        style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
            SettingsGroup { Column(Modifier.padding(16.dp)) { AppUpdateSection(false) } }
        }
        return
    }
    PageColumn {
        Row(horizontalArrangement = Arrangement.spacedBy(48.dp)) {
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(16.dp)) {
        AppText(ui(R.string.app_language), isTv)
        ChoiceRow(AppLanguage.entries.map { it.label }, Language.current(context).label, isTv) { label ->
            var owner = context
            while (owner is android.content.ContextWrapper && owner !is android.app.Activity) owner = owner.baseContext
            (owner as? android.app.Activity)?.let { Language.select(it, AppLanguage.entries.first { language -> language.label == label }) }
        }
        AppText(ui(R.string.appearance), isTv)
        ChoiceRow(AppTheme.entries.map { ui(it.labelId) }, ui(Appearance.current(context).labelId), isTv) { label ->
            Appearance.select(context, AppTheme.entries.first { ui(it.labelId) == label })
        }
        AppText(ui(R.string.about_app), true)
        AppText(ui(R.string.app_version, BuildConfig.VERSION_NAME), true)
        DataContent(version, true, { revision++ }) { AppText(ui(R.string.core_version, (it as? JSONObject)?.optString("version").orEmpty()), true) }
        AppUpdateSection(true)
        }
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(16.dp)) {
        AppText(ui(R.string.storage), isTv)
        AppButton(ui(R.string.clear_catalog_cache), isTv, {
            run { core.value(CoreCommand.ClearCache); action.message = ui(R.string.catalog_cache_cleared) }
        }, enabled = !action.busy)
        AppButton(ui(R.string.clear_playback_files), isTv, {
            run { core.value(CoreCommand.ClearPlaybackAssets); action.message = ui(R.string.playback_files_cleared) }
        }, enabled = !action.busy)
        AppText(ui(R.string.downloads_kept), isTv)
        ActionStatus(action, isTv)
        }
        }
    }
}
