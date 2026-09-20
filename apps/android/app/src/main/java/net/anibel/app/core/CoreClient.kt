package net.anibel.app

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.asExecutor
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.File
import java.util.concurrent.atomic.AtomicLong
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

internal object CoreNative {
    init { System.loadLibrary("anibel_core") }
    @JvmStatic external fun init(config: ByteArray): Long
    @JvmStatic external fun begin(handle: Long, id: Long): Int
    @JvmStatic external fun cancel(handle: Long, id: Long)
    @JvmStatic external fun call(handle: Long, request: ByteArray): ByteArray?
    @JvmStatic external fun shutdown(handle: Long)
}

internal class CoreClient(private val directory: File, private val credentials: CredentialStore? = null) {
    private val snapshotsOwner = lazy { CoreSnapshots(this) }
    val snapshots: CoreSnapshots get() = snapshotsOwner.value
    private val ids = AtomicLong(0)
    private val sessionLock = Any()
    private enum class State { Open, Closing, Closed }
    private val lifecycle = Any()
    private val closeLock = Mutex()
    private val drained = CompletableDeferred<Unit>()
    private var state = State.Open
    private var activeRequests = 0
    private val nativeHandle = lazy {
        check(directory.isDirectory || directory.mkdirs()) { ui(R.string.core_folder_error) }
        val result = CoreNative.init(JSONObject().put("dataDir", directory.path).toString().toByteArray(Charsets.UTF_8))
        check(result > 0) { ui(R.string.core_start_error) }
        try {
            val capabilities = decode(CoreNative.call(result, request(0, CoreCommand.Capabilities, JSONObject(), false)), 0) as JSONObject
            check(capabilities.getInt("protocolVersion") == 2) { ui(R.string.core_protocol_error) }
            credentials?.read()?.let { saved ->
                decode(CoreNative.call(result, request(-1, CoreCommand.SetToken, saved, false)), -1)
            }
            result
        } catch (error: Throwable) {
            CoreNative.shutdown(result)
            throw error
        }
    }

    suspend fun call(op: CoreCommand, args: JSONObject = JSONObject(), reload: Boolean = false): JSONObject =
        value(op, args, reload) as? JSONObject ?: error(ui(R.string.core_no_data))

    suspend fun value(op: CoreCommand, args: JSONObject = JSONObject(), reload: Boolean = false): Any? =
        withContext(Dispatchers.IO) {
            val id = ids.updateAndGet { Math.addExact(it, 1L) }
            val bytes = request(id, op, args, reload)
            val activeHandle = synchronized(lifecycle) {
                check(state == State.Open) { "Core is closed" }
                val handle = nativeHandle.value
                check(CoreNative.begin(handle, id) == 1) { ui(R.string.core_busy) }
                activeRequests++
                handle
            }
            suspendCancellableCoroutine { continuation ->
                continuation.invokeOnCancellation { CoreNative.cancel(activeHandle, id) }
                // Always enter call, even after cancellation: the ABI releases the reservation there.
                Dispatchers.IO.asExecutor().execute {
                    try {
                        fun execute(): Any? {
                            val result = decode(CoreNative.call(activeHandle, bytes), id)
                            if (op == CoreCommand.Login && result is JSONObject) credentials?.write(result)
                            if (op == CoreCommand.Logout || op == CoreCommand.Session && result is JSONObject && !result.optBoolean("authenticated")) credentials?.clear()
                            return result
                        }
                        val value = if (op in listOf(CoreCommand.Login, CoreCommand.Logout, CoreCommand.Session)) synchronized(sessionLock) { execute() } else execute()
                        continuation.resume(value)
                    } catch (error: Throwable) {
                        continuation.resumeWithException(error)
                    } finally {
                        synchronized(lifecycle) {
                            activeRequests--
                            if (state == State.Closing && activeRequests == 0) drained.complete(Unit)
                        }
                    }
                }
            }
        }

    suspend fun close() = withContext(NonCancellable + Dispatchers.IO) {
        closeLock.withLock {
            if (snapshotsOwner.isInitialized()) snapshotsOwner.value.close()
            synchronized(lifecycle) {
                if (state != State.Closed) state = State.Closing
                if (activeRequests == 0) drained.complete(Unit)
            }
            drained.await()
            synchronized(lifecycle) {
                if (state != State.Closed && nativeHandle.isInitialized()) CoreNative.shutdown(nativeHandle.value)
                state = State.Closed
            }
        }
    }

    private fun request(id: Long, op: CoreCommand, args: JSONObject, reload: Boolean) =
        JSONObject().put("id", id).put("op", op.wire).put("args", args)
            .put("cache", if (reload) "reload" else "default").toString().toByteArray(Charsets.UTF_8)

    private fun decode(bytes: ByteArray?, id: Long): Any? {
        checkNotNull(bytes) { ui(R.string.core_empty_response) }
        val response = JSONObject(bytes.toString(Charsets.UTF_8))
        check(response.getLong("id") == id) { ui(R.string.core_response_error) }
        if (!response.getBoolean("ok")) {
            val failure = response.optJSONObject("error")
            throw CoreFailure(failure?.optString("code") ?: "internal", failure?.optString("message") ?: ui(R.string.request_failed))
        }
        return response.opt("value").takeUnless { it == JSONObject.NULL }
    }
}

internal class CoreFailure(val code: String, message: String) : IllegalStateException(message)
