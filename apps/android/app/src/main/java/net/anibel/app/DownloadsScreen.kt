package net.anibel.app

import android.app.*
import android.content.Intent
import android.os.IBinder
import android.os.Build
import android.Manifest
import android.content.pm.PackageManager
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
import kotlinx.coroutines.*
import org.json.JSONObject

@Composable
internal fun rememberDownloadEnqueue(): suspend (String, JSONObject) -> Unit {
    val core = LocalCore.current
    val context = LocalContext.current.applicationContext
    val permission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { }
    return { kind, request ->
        core.value("downloadEnqueue", json("kind" to kind, "request" to request))
        if (Build.VERSION.SDK_INT >= 33 && context.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED)
            permission.launch(Manifest.permission.POST_NOTIFICATIONS)
        context.startForegroundService(Intent(context, DownloadService::class.java))
    }
}

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
                while (true) {
                    val active = core.call("downloads").optJSONArray("items").objects()
                        .filter { it.optString("status") in listOf("queued", "downloading") }
                    if (active.isEmpty()) break
                    if (Build.VERSION.SDK_INT < 33 || checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED)
                        getSystemService(NotificationManager::class.java).notify(1, notification(ui(R.string.active_downloads, active.size, active.first().optString("title"))))
                    delay(1500)
                }
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (_: Exception) { /* Queue and errors remain visible in Downloads. */ }
            finally { stopForeground(STOP_FOREGROUND_REMOVE); stopSelf() }
        }
        return START_NOT_STICKY
    }
    override fun onTimeout(startId: Int, fgsType: Int) { stopSelf() }
    override fun onDestroy() { scope.cancel(); super.onDestroy() }
}

internal enum class DownloadFilter { All, Active, Ready }

@Composable
internal fun DownloadsScreen(isTv: Boolean) {
    val core = LocalCore.current
    val navigator = LocalNavigator.current
    val context = LocalContext.current
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    var data by remember { mutableStateOf<RemoteData>(RemoteData.Loading) }
    var revision by remember { mutableIntStateOf(0) }
    var filter by rememberSaveable { mutableStateOf(DownloadFilter.All) }
    var query by rememberSaveable { mutableStateOf("") }
    var deleting by remember { mutableStateOf<JSONObject?>(null) }
    val (action, run) = rememberAction()
    var exportId by rememberSaveable { mutableStateOf<String?>(null) }
    val exportFolder = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocumentTree()) { uri ->
        val id = exportId
        exportId = null
        if (uri != null && id != null) run {
            exportDownload(context, core, id, uri)
            action.message = ui(R.string.export_saved)
        }
    }
    LaunchedEffect(revision, lifecycle) {
        lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            while (true) {
                data = try { RemoteData.Ready(core.call("downloads")) }
                catch (cancelled: CancellationException) { throw cancelled }
                catch (error: Exception) { RemoteData.Failed(error.message ?: ui(R.string.load_downloads_failed)) }
                delay(1500)
            }
        }
    }
    RefreshablePage(isTv, data == RemoteData.Loading && revision > 0, { data = RemoteData.Loading; revision++ }, Modifier.fillMaxSize()) {
    Column(Modifier.fillMaxSize().padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        OutlinedTextField(query, { query = it }, Modifier.fillMaxWidth(), label = { Text(ui(R.string.find_download)) }, singleLine = true)
        ChoiceRow(DownloadFilter.entries.map { it.name }, filter.name, isTv) { filter = DownloadFilter.valueOf(it) }
        ActionStatus(action, isTv)
        DataContent(data, isTv, { revision++ }, loadingModifier = Modifier.fillMaxSize()) { value ->
            val all = (value as? JSONObject)?.optJSONArray("items").objects()
            val rows = all.filter { item ->
                val status = item.optString("status")
                (filter == DownloadFilter.All || filter == DownloadFilter.Active && status in listOf("queued", "downloading") || filter == DownloadFilter.Ready && status == "completed") &&
                    (item.optString("title") + item.optString("subtitle")).contains(query, ignoreCase = true)
            }
            AppText(ui(R.string.downloads_summary, all.size, android.text.format.Formatter.formatFileSize(context, all.sumOf { it.optLong("diskBytes") })), isTv)
            LazyColumn(Modifier.weight(1f), contentPadding = PaddingValues(8.dp), verticalArrangement = Arrangement.spacedBy(20.dp)) {
                if (rows.isEmpty()) item { AppText(ui(R.string.empty_downloads), isTv) }
                items(rows, key = { it.getString("id") }) { item ->
                    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        AppText(item.optString("title"), isTv)
                        AppText("${item.optString("subtitle")} · ${keyLabel(item.optString("status"))} · ${(item.optDouble("progress", 0.0) * 100).toInt()}%", isTv)
                        when (item.optString("status")) {
                            "queued" -> LinearProgressIndicator(Modifier.fillMaxWidth())
                            "downloading" -> LinearProgressIndicator(progress = {
                                item.optDouble("progress", 0.0).takeIf { it.isFinite() }?.coerceIn(0.0, 1.0)?.toFloat() ?: 0f
                            }, modifier = Modifier.fillMaxWidth())
                        }
                        item.text("error")?.let { AppText(it, isTv) }
                        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                            if (item.optBoolean("canPlay")) AppButton(ui(R.string.open), isTv, {
                                if (item.optString("kind") == "manga") navigator.open(Screen.Reader, json("slug" to item.text("slug"),
                                    "chapter" to item.optDouble("chapter"), "chapters" to item.optJSONArray("chapterList")))
                                else navigator.open(Screen.Player, json("downloadId" to item.getString("id")))
                            })
                            if (item.optBoolean("canRetry")) AppButton(ui(R.string.retry), isTv, { run {
                                core.value("downloadChange", json("id" to item.getString("id"), "action" to "retry"))
                                context.startForegroundService(Intent(context, DownloadService::class.java))
                            } }, enabled = !action.busy)
                            if (item.optString("status") in listOf("queued", "downloading")) AppButton(ui(R.string.cancel), isTv, { run {
                                core.value("downloadChange", json("id" to item.getString("id"), "action" to "cancel"))
                            } }, enabled = !action.busy)
                            AppButton(ui(R.string.delete), isTv, { deleting = item }, enabled = !action.busy)
                            if (item.optBoolean("canSave")) AppButton(ui(R.string.save_folder), isTv, {
                                exportId = item.getString("id")
                                exportFolder.launch(null)
                            }, enabled = !action.busy)
                        }
                    }
                }
            }
        }
    }
    }
    deleting?.let { item ->
        AlertDialog(onDismissRequest = { deleting = null }, title = { Text(ui(R.string.delete_download)) },
            text = { Text(item.optString("title")) }, confirmButton = {
                AppButton(ui(R.string.delete), isTv, { run {
                    core.value("downloadChange", json("id" to item.getString("id"), "action" to "delete")); deleting = null
                } }, enabled = !action.busy)
            }, dismissButton = { AppButton(ui(R.string.cancel), isTv, { deleting = null }, enabled = !action.busy) })
    }
}
