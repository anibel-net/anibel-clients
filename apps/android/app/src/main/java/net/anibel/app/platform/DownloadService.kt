package net.anibel.app

import android.app.*
import android.content.Intent
import android.os.IBinder
import android.os.Build
import android.Manifest
import android.content.pm.PackageManager
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.*
import androidx.compose.runtime.*
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.first

class DownloadService : Service() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private var worker: Job? = null
    override fun onBind(intent: Intent?): IBinder? = null
    override fun onCreate() {
        super.onCreate()
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(NotificationChannel("downloads", ui(R.string.downloads), NotificationManager.IMPORTANCE_LOW))
        startForeground(1, notification(ui(R.string.preparing_downloads)))
    }
    private fun notification(text: String): Notification {
        val tv = packageManager.hasSystemFeature("android.software.leanback")
        val intent = Intent(this, if (tv) TvActivity::class.java else MobileActivity::class.java)
        return Notification.Builder(this, "downloads").setSmallIcon(R.drawable.ic_download)
            .setContentTitle(ui(R.string.download_notification)).setContentText(text).setOngoing(true)
            .setContentIntent(PendingIntent.getActivity(this, 0, intent, PendingIntent.FLAG_IMMUTABLE)).build()
    }
    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (worker?.isActive != true) worker = scope.launch {
            try {
                val core = (application as AnibelApplication).core
                core.value(CoreCommand.DownloadsResume)
                core.snapshots.downloads.refresh()
                core.snapshots.downloads.state.first { result ->
                    if (result == null) false else {
                        val active = result.getOrThrow().optJSONArray("items").objects()
                            .filter { it.optString("status") in listOf("queued", "downloading") }
                        if (active.isNotEmpty() && (Build.VERSION.SDK_INT < 33 || checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED))
                            getSystemService(NotificationManager::class.java).notify(1, notification(ui(R.string.active_downloads, active.size, active.first().optString("title"))))
                        active.isEmpty()
                    }
                }
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (_: Exception) { /* Queue and errors remain visible in Downloads. */ }
            finally {
                withContext(NonCancellable) {
                    runCatching { (application as AnibelApplication).core.value(CoreCommand.DownloadsSuspend) }
                }
                stopForeground(STOP_FOREGROUND_REMOVE); stopSelf()
            }
        }
        return START_NOT_STICKY
    }
    override fun onTimeout(startId: Int, fgsType: Int) { stopSelf() }
    override fun onDestroy() { scope.cancel(); super.onDestroy() }
}
