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
        PageColumn(onRefresh = if (isTv) null else { { revision++ } }, refreshing = revision > 0 && version == RemoteData.Loading) {
            Text(ui(R.string.app_language), style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.primary)
            Column {
                AppLanguage.entries.forEach { language ->
                    ListItem(headlineContent = { Text(language.label) },
                        leadingContent = { RadioButton(Language.current(context) == language, onClick = null) },
                        modifier = Modifier.clickable {
                            var owner = context
                            while (owner is android.content.ContextWrapper && owner !is android.app.Activity) owner = owner.baseContext
                            (owner as? android.app.Activity)?.let { Language.select(it, language) }
                        })
                }
            }
            HorizontalDivider()
            ListItem(headlineContent = { Text(ui(R.string.appearance)) }, supportingContent = { Text(ui(R.string.system_theme_hint)) })
            HorizontalDivider()
            Text(ui(R.string.storage), style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.primary)
            Column {
                ListItem(headlineContent = { Text(ui(R.string.clear_catalog_cache)) }, modifier = Modifier.clickable(enabled = !action.busy) {
                    run { core.value(CoreCommand.ClearCache); action.message = ui(R.string.catalog_cache_cleared) }
                })
                ListItem(headlineContent = { Text(ui(R.string.clear_playback_files)) }, supportingContent = { Text(ui(R.string.downloads_kept)) },
                    modifier = Modifier.clickable(enabled = !action.busy) {
                        run { core.value(CoreCommand.ClearPlaybackAssets); action.message = ui(R.string.playback_files_cleared) }
                    })
            }
            ActionStatus(action, false)
            HorizontalDivider()
            ListItem(headlineContent = { Text(ui(R.string.about_app)) }, supportingContent = { Text(ui(R.string.app_version)) })
            DataContent(version, false, { revision++ }) { Text(ui(R.string.core_version, (it as? JSONObject)?.optString("version").orEmpty()),
                style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant) }
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
        AppText(ui(R.string.system_theme_hint), isTv)
        AppText(ui(R.string.about_app), true)
        AppText(ui(R.string.app_version), true)
        DataContent(version, true, { revision++ }) { AppText(ui(R.string.core_version, (it as? JSONObject)?.optString("version").orEmpty()), true) }
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
