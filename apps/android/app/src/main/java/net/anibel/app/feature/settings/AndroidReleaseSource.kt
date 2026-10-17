package net.anibel.app

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.InputStream
import java.net.URL
import java.security.MessageDigest
import javax.net.ssl.HttpsURLConnection
import kotlin.coroutines.coroutineContext

internal data class AndroidUpdate(val version: ReleaseVersion, val versionCode: Long, val size: Long, val sha256: String)

/** Public GitHub releases only. No account or API credentials enter the app. */
internal object AndroidReleaseSource {
    const val repository = "anibel-net/anibel-clients"
    const val apkName = "Anibel-Android.apk"
    const val maxApkSize = 512L * 1024 * 1024
    private fun assetUrl(version: ReleaseVersion, name: String) =
        "https://github.com/$repository/releases/download/android-v$version/$name"

    internal fun releaseVersions(releases: JSONArray, preview: Boolean): List<ReleaseVersion> =
        (0 until releases.length()).mapNotNull { index ->
            val release = releases.getJSONObject(index)
            if (release.optBoolean("draft", true)) return@mapNotNull null
            val tag = release.optString("tag_name")
            if (!tag.startsWith("android-v")) return@mapNotNull null
            val version = ReleaseVersion.parse(tag.removePrefix("android-v")) ?: return@mapNotNull null
            if (!preview && (release.optBoolean("prerelease", true) || version.beta != null)) return@mapNotNull null
            val names = release.optJSONArray("assets").let { assets ->
                if (assets == null) emptyList() else (0 until assets.length()).map { assets.getJSONObject(it).optString("name") }
            }
            version.takeIf { apkName in names && "android-update.json" in names }
        }

    internal fun parseMetadata(text: String, version: ReleaseVersion): AndroidUpdate {
        val data = JSONObject(text)
        require(data.getString("versionName") == version.toString() && data.getString("apk") == apkName)
        val code = requireNotNull(data.optString("versionCode").toLongOrNull())
        val size = requireNotNull(data.optString("size").toLongOrNull())
        val sha256 = data.getString("sha256")
        require(code in 1..2_100_000_000L && size in 1..maxApkSize && sha256.matches(Regex("[0-9a-f]{64}")))
        return AndroidUpdate(version, code, size, sha256)
    }

    suspend fun findUpdate(current: ReleaseVersion, currentCode: Long): AndroidUpdate? = withContext(Dispatchers.IO) {
        var newest: ReleaseVersion? = null
        for (page in 1..10) {
            val releases = JSONArray(readText("https://api.github.com/repos/$repository/releases?per_page=100&page=$page", 4 * 1024 * 1024))
            newest = (releaseVersions(releases, current.beta != null) + listOfNotNull(newest)).maxOrNull()
            if (releases.length() < 100) {
                if (newest == null || newest <= current) return@withContext null
                val update = parseMetadata(readText(assetUrl(newest, "android-update.json"), 16 * 1024), newest)
                require(update.versionCode > currentCode) { "The release version code must increase." }
                return@withContext update
            }
        }
        error("Too many release pages. Try again after the release feed is corrected.")
    }

    suspend fun download(update: AndroidUpdate, target: File) = withContext(Dispatchers.IO) {
        val partial = File(target.parentFile, "update.part")
        try {
            check(target.parentFile?.isDirectory == true || target.parentFile?.mkdirs() == true)
            connection(assetUrl(update.version, apkName)) { connection ->
                val length = connection.contentLengthLong
                require(length == -1L || length == update.size)
                connection.inputStream.use { input ->
                    copyApk(input, update, partial)
                }
            }
            coroutineContext.ensureActive()
            check(!target.exists() || target.delete())
            check(partial.renameTo(target))
        } finally { partial.delete() }
    }

    internal suspend fun copyApk(input: InputStream, update: AndroidUpdate, partial: File) {
        val digest = MessageDigest.getInstance("SHA-256")
        partial.outputStream().use { output ->
            val buffer = ByteArray(64 * 1024)
            var total = 0L
            while (true) {
                coroutineContext.ensureActive()
                val count = input.read(buffer)
                if (count == -1) break
                require(count.toLong() <= update.size - total) { "The APK exceeds its expected size." }
                total += count
                digest.update(buffer, 0, count)
                output.write(buffer, 0, count)
            }
            require(total == update.size)
        }
        require(digest.digest().joinToString("") { "%02x".format(it) } == update.sha256) { "The APK checksum does not match." }
    }

    private suspend fun readText(url: String, limit: Int): String = connection(url) { connection ->
        connection.inputStream.use { input ->
            val output = java.io.ByteArrayOutputStream()
            val buffer = ByteArray(8192)
            while (true) {
                coroutineContext.ensureActive()
                val count = input.read(buffer)
                if (count == -1) break
                require(count <= limit - output.size()) { "The release response is too large." }
                output.write(buffer, 0, count)
            }
            output.toString(Charsets.UTF_8.name())
        }
    }

    private suspend fun <T> connection(url: String, read: suspend (HttpsURLConnection) -> T): T {
        coroutineContext.ensureActive()
        val connection = URL(url).openConnection() as HttpsURLConnection
        try {
            connection.connectTimeout = 15_000
            connection.readTimeout = 15_000
            connection.setRequestProperty("User-Agent", "Anibel-Android/${BuildConfig.VERSION_NAME}")
            connection.setRequestProperty("Accept", "application/vnd.github+json")
            require(connection.responseCode == 200 && connection.url.protocol == "https") { "The release request failed." }
            return read(connection)
        } finally { connection.disconnect() }
    }
}
