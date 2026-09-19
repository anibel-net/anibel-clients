package net.anibel.app

import android.app.Application
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.asExecutor
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.File
import okio.Path.Companion.toOkioPath
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

class AnibelApplication : Application(), coil3.SingletonImageLoader.Factory {
    internal val episodeArtwork = EpisodeArtworkCache()
    override fun newImageLoader(context: android.content.Context): coil3.ImageLoader =
        coil3.ImageLoader.Builder(context)
            .memoryCache { coil3.memory.MemoryCache.Builder().maxSizeBytes(64L * 1024 * 1024).build() }
            .diskCache { coil3.disk.DiskCache.Builder()
                .directory(File(context.cacheDir, "image_cache").toOkioPath())
                .maxSizeBytes(256L * 1024 * 1024).build() }
            .build()
    override fun onCreate() { super.onCreate(); Language.initialize(this) }
    // One core and cache for the lifetime of this app process.
    internal val core by lazy { CoreClient(File(filesDir, "core"), CredentialStore(filesDir)) }
}

internal class CoreClient(private val directory: File, private val credentials: CredentialStore? = null) {
    private val ids = AtomicLong(0)
    private val sessionLock = Any()
    private val handle: Long by lazy {
        check(directory.isDirectory || directory.mkdirs()) { ui(R.string.core_folder_error) }
        val result = CoreNative.init(JSONObject().put("dataDir", directory.path).toString().toByteArray(Charsets.UTF_8))
        check(result > 0) { ui(R.string.core_start_error) }
        try {
            val capabilities = decode(CoreNative.call(result, request(0, "capabilities", JSONObject(), false)), 0) as JSONObject
            check(capabilities.getInt("protocolVersion") == 2) { ui(R.string.core_protocol_error) }
            credentials?.read()?.let { saved ->
                decode(CoreNative.call(result, request(-1, "setToken", saved, false)), -1)
            }
            result
        } catch (error: Throwable) {
            CoreNative.shutdown(result)
            throw error
        }
    }

    suspend fun call(op: String, args: JSONObject = JSONObject(), reload: Boolean = false): JSONObject =
        value(op, args, reload) as? JSONObject ?: error(ui(R.string.core_no_data))

    suspend fun value(op: String, args: JSONObject = JSONObject(), reload: Boolean = false): Any? =
        withContext(Dispatchers.IO) {
            val activeHandle = handle
            val id = ids.updateAndGet { Math.addExact(it, 1L) }
            val bytes = request(id, op, args, reload)
            check(CoreNative.begin(activeHandle, id) == 1) { ui(R.string.core_busy) }
            suspendCancellableCoroutine { continuation ->
                continuation.invokeOnCancellation { CoreNative.cancel(activeHandle, id) }
                // Always enter call, even after cancellation: the ABI releases the reservation there.
                Dispatchers.IO.asExecutor().execute {
                    try {
                        fun execute(): Any? {
                            val result = decode(CoreNative.call(activeHandle, bytes), id)
                            if (op == "login" && result is JSONObject) credentials?.write(result)
                            if (op == "logout" || op == "session" && result is JSONObject && !result.optBoolean("authenticated")) credentials?.clear()
                            return result
                        }
                        val value = if (op in listOf("login", "logout", "session")) synchronized(sessionLock) { execute() } else execute()
                        continuation.resume(value)
                    } catch (error: Throwable) {
                        continuation.resumeWithException(error)
                    }
                }
            }
        }

    private fun request(id: Long, op: String, args: JSONObject, reload: Boolean) =
        JSONObject().put("id", id).put("op", op).put("args", args)
            .put("cache", if (reload) "reload" else "default").toString().toByteArray(Charsets.UTF_8)

    private fun decode(bytes: ByteArray?, id: Long): Any? {
        checkNotNull(bytes) { ui(R.string.core_empty_response) }
        val response = JSONObject(bytes.toString(Charsets.UTF_8))
        check(response.getLong("id") == id) { ui(R.string.core_response_error) }
        check(response.getBoolean("ok")) {
            response.optJSONObject("error")?.optString("message") ?: ui(R.string.request_failed)
        }
        return response.opt("value").takeUnless { it == JSONObject.NULL }
    }
}
