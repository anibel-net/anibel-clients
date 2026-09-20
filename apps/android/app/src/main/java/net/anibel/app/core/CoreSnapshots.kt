package net.anibel.app

import kotlinx.coroutines.*
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import org.json.JSONObject

/** One subscription-driven observer per core command, shared by screens and the service. */
internal class CoreSnapshots(core: CoreClient) {
    private val job = SupervisorJob()
    private val scope = CoroutineScope(job + Dispatchers.Default)
    val session = SnapshotPoller(core, CoreCommand.Session, scope) { 3000L }
    val downloads = SnapshotPoller(core, CoreCommand.Downloads, scope) { value ->
        if (value.optJSONArray("items").objects().any { it.optString("status") in listOf("queued", "downloading") }) 1000L else 3000L
    }
    suspend fun close() { job.cancelAndJoin() }
}
internal class SnapshotPoller(private val core: CoreClient, private val command: CoreCommand,
    scope: CoroutineScope, private val interval: (JSONObject) -> Long) {
    private val refreshLock = Mutex()
    private val current = MutableStateFlow<Result<JSONObject>?>(null)
    val state: StateFlow<Result<JSONObject>?> = current.asStateFlow()
    init {
        scope.launch {
            current.subscriptionCount.collectLatest { subscribers ->
                if (subscribers > 0) while (true) {
                    refresh()
                    delay(current.value?.getOrNull()?.let(interval) ?: 3000L)
                }
            }
        }
    }
    suspend fun refresh() = refreshLock.withLock {
        current.value = try { Result.success(core.call(command)) }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (error: Exception) { Result.failure(error) }
    }
}
