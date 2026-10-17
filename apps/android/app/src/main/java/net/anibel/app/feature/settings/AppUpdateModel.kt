package net.anibel.app

import android.app.Application
import android.content.Context
import android.content.Intent
import android.content.pm.PackageInfo
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.core.content.FileProvider
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
import kotlin.coroutines.coroutineContext

internal sealed interface AppUpdateState {
    data object Unavailable : AppUpdateState
    data object Idle : AppUpdateState
    data object Checking : AppUpdateState
    data object Current : AppUpdateState
    data class Available(val update: AndroidUpdate) : AppUpdateState
    data object Downloading : AppUpdateState
    data class Ready(val version: String) : AppUpdateState
    data object Failed : AppUpdateState
}

internal class AppUpdateModel(application: Application) : AndroidViewModel(application) {
    private val apk = File(application.filesDir, "app-updates/update.apk")
    var state by mutableStateOf<AppUpdateState>(if (BuildConfig.DEBUG) AppUpdateState.Unavailable else AppUpdateState.Checking)
        private set
    val busy: Boolean get() = state == AppUpdateState.Checking || state == AppUpdateState.Downloading

    init {
        if (!BuildConfig.DEBUG) viewModelScope.launch {
            state = withContext(Dispatchers.IO) {
                File(apk.parentFile, "update.part").delete()
                if (!apk.isFile) AppUpdateState.Idle
                else try { AppUpdateState.Ready(checkUpdateApk(application, apk).versionName.orEmpty()) }
                catch (_: Exception) { apk.delete(); AppUpdateState.Idle }
            }
        }
    }

    fun check() {
        if (busy || state == AppUpdateState.Unavailable || state is AppUpdateState.Ready) return
        state = AppUpdateState.Checking
        viewModelScope.launch {
            try {
                val update = AndroidReleaseSource.findUpdate(checkNotNull(ReleaseVersion.parse(BuildConfig.VERSION_NAME)), BuildConfig.VERSION_CODE.toLong())
                state = update?.let(AppUpdateState::Available) ?: AppUpdateState.Current
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (_: Exception) { state = AppUpdateState.Failed }
        }
    }

    fun download() {
        val update = (state as? AppUpdateState.Available)?.update ?: return
        state = AppUpdateState.Downloading
        viewModelScope.launch {
            try {
                AndroidReleaseSource.download(update, apk)
                withContext(Dispatchers.IO) {
                    val info = checkUpdateApk(getApplication(), apk)
                    require(info.versionName == update.version.toString() && packageVersionCode(info) == update.versionCode)
                }
                coroutineContext.ensureActive()
                state = AppUpdateState.Ready(update.version.toString())
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (_: Exception) { withContext(Dispatchers.IO) { apk.delete() }; state = AppUpdateState.Failed }
        }
    }

    fun install(context: Context) {
        if (state !is AppUpdateState.Ready) return
        try {
            if (!context.packageManager.canRequestPackageInstalls()) {
                context.startActivity(Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:${context.packageName}")))
                return
            }
            val uri = FileProvider.getUriForFile(context, "${context.packageName}.updates", apk)
            context.startActivity(Intent(Intent.ACTION_VIEW).setDataAndType(uri, "application/vnd.android.package-archive")
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION))
        } catch (_: Exception) { state = AppUpdateState.Failed }
    }
}

@Suppress("DEPRECATION")
internal fun packageVersionCode(info: PackageInfo): Long = if (Build.VERSION.SDK_INT >= 28) info.longVersionCode else info.versionCode.toLong()

@Suppress("DEPRECATION")
internal fun checkUpdateApk(context: Context, apk: File): PackageInfo {
    val flags = if (Build.VERSION.SDK_INT >= 28) PackageManager.GET_SIGNING_CERTIFICATES else PackageManager.GET_SIGNATURES
    val current = context.packageManager.getPackageInfo(context.packageName, flags)
    val next = checkNotNull(context.packageManager.getPackageArchiveInfo(apk.path, flags))
    require(next.packageName == current.packageName && packageVersionCode(next) > packageVersionCode(current))
    fun signers(info: PackageInfo) = if (Build.VERSION.SDK_INT >= 28) info.signingInfo?.apkContentsSigners else info.signatures
    val expected = checkNotNull(signers(current)).toSet()
    require(expected.isNotEmpty() && signers(next)?.toSet() == expected) { "The APK signing certificate does not match." }
    return next
}
