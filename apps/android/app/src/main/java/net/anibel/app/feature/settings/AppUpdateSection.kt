package net.anibel.app

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import androidx.lifecycle.viewmodel.compose.viewModel

@Composable
internal fun AppUpdateSection(isTv: Boolean, model: AppUpdateModel = viewModel()) {
    val context = LocalContext.current
    val state = model.state
    val message = when (state) {
        AppUpdateState.Unavailable -> ui(R.string.app_update_debug)
        AppUpdateState.Idle -> ui(R.string.app_update_hint)
        AppUpdateState.Checking -> ui(R.string.app_update_checking)
        AppUpdateState.Current -> ui(R.string.app_update_current)
        is AppUpdateState.Available -> ui(R.string.app_update_available, state.update.version.toString())
        AppUpdateState.Downloading -> ui(R.string.app_update_downloading)
        is AppUpdateState.Ready -> ui(R.string.app_update_ready, state.version)
        AppUpdateState.Failed -> ui(R.string.app_update_failed)
    }
    Column(verticalArrangement = Arrangement.spacedBy(12.dp), modifier = Modifier.testTag("app_updates")) {
        AppText(message, isTv, Modifier.testTag("app_update_status"))
        AppButton(ui(when (state) {
            is AppUpdateState.Available -> R.string.app_update_download
            is AppUpdateState.Ready -> R.string.app_update_install
            else -> R.string.app_update_check
        }), isTv, {
            when (state) {
                is AppUpdateState.Available -> model.download()
                is AppUpdateState.Ready -> model.install(context)
                else -> model.check()
            }
        }, Modifier.testTag("app_update_action"), enabled = !model.busy && state != AppUpdateState.Unavailable)
        if (state is AppUpdateState.Ready) AppText(ui(R.string.app_update_install_hint), isTv)
    }
}
