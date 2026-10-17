package net.anibel.app

import androidx.test.core.app.ApplicationProvider
import androidx.test.platform.app.InstrumentationRegistry
import kotlinx.coroutines.runBlocking
import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import java.io.File
import java.security.MessageDigest

/** Local release fixtures only. These checks make no GitHub or Anibel requests. */
class AndroidUpdateTest {
    private fun release(tag: String, draft: Boolean = false, preview: Boolean = false, apk: Boolean = true) = JSONObject().apply {
        put("tag_name", tag); put("draft", draft); put("prerelease", preview)
        put("assets", JSONArray().apply {
            if (apk) put(JSONObject().put("name", "Anibel-Android.apk"))
            put(JSONObject().put("name", "android-update.json"))
        })
    }

    @Test fun channelsIgnoreWindowsDraftsAndIncompleteReleases() {
        val releases = JSONArray().apply {
            put(release("windows-v99.0.0"))
            put(release("android-v1.0.0"))
            put(release("android-v1.1.0-beta.1", preview = true))
            put(release("android-v2.0.0", draft = true))
            put(release("android-v3.0.0", apk = false))
        }
        assertEquals(listOf("1.0.0"), AndroidReleaseSource.releaseVersions(releases, false).map { it.toString() })
        assertEquals(listOf("1.0.0", "1.1.0-beta.1"), AndroidReleaseSource.releaseVersions(releases, true).map { it.toString() })
    }

    @Test fun metadataRejectsWrongVersionsFilesAndBounds() {
        val version = checkNotNull(ReleaseVersion.parse("1.0.0"))
        val data = JSONObject().put("versionName", "1.0.0").put("versionCode", 5).put("apk", "Anibel-Android.apk")
            .put("size", 1024).put("sha256", "a".repeat(64))
        assertEquals(5L, AndroidReleaseSource.parseMetadata(data.toString(), version).versionCode)
        listOf("versionName" to "2.0.0", "versionCode" to 0, "versionCode" to 1.5, "versionCode" to 2_100_000_001L,
            "apk" to "../other.apk", "size" to 0, "size" to AndroidReleaseSource.maxApkSize + 1, "sha256" to "invalid").forEach { (key, value) ->
            val changed = JSONObject(data.toString()).put(key, value)
            try { AndroidReleaseSource.parseMetadata(changed.toString(), version); fail("Accepted invalid $key") }
            catch (_: IllegalArgumentException) { }
        }
    }

    @Test fun sameVersionAndInvalidApksCannotBeUpdates() {
        val app = ApplicationProvider.getApplicationContext<AnibelApplication>()
        try { checkUpdateApk(app, File(app.applicationInfo.sourceDir)); fail("Accepted the installed version as an update") }
        catch (_: IllegalArgumentException) { }
        val invalid = File(app.cacheDir, "invalid-update.apk")
        try {
            invalid.writeText("not an APK")
            try { checkUpdateApk(app, invalid); fail("Accepted an invalid APK") }
            catch (_: IllegalStateException) { }
        } finally { invalid.delete() }
    }

    @Test fun downloadsRejectShortLongAndCorruptContent() = runBlocking {
        val app = ApplicationProvider.getApplicationContext<AnibelApplication>()
        val partial = File(app.cacheDir, "update-fixture.part")
        val bytes = "local APK transport fixture".toByteArray()
        val digest = MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { "%02x".format(it) }
        val update = AndroidUpdate(checkNotNull(ReleaseVersion.parse("1.0.0")), 2, bytes.size.toLong(), digest)
        try {
            AndroidReleaseSource.copyApk(bytes.inputStream(), update, partial)
            assertArrayEquals(bytes, partial.readBytes())
            listOf(update.copy(size = update.size - 1), update.copy(size = update.size + 1), update.copy(sha256 = "0".repeat(64))).forEach { invalid ->
                try { AndroidReleaseSource.copyApk(bytes.inputStream(), invalid, partial); fail("Accepted invalid APK content") }
                catch (_: IllegalArgumentException) { }
            }
        } finally { partial.delete() }
    }

    @Test fun onlyNewerApksWithTheSamePackageAndSigningKeyAreAccepted() {
        val app = ApplicationProvider.getApplicationContext<AnibelApplication>()
        val assets = InstrumentationRegistry.getInstrumentation().context.assets
        val apk = File(app.cacheDir, "signed-update-fixture.apk")
        try {
            for (name in listOf("valid-update", "wrong-signer", "wrong-package")) {
                assets.open("$name.apk").use { input -> apk.outputStream().use { input.copyTo(it) } }
                if (name == "valid-update") {
                    val info = checkUpdateApk(app, apk)
                    assertEquals(BuildConfig.VERSION_CODE.toLong() + 1, packageVersionCode(info))
                } else {
                    try { checkUpdateApk(app, apk); fail("Accepted $name") }
                    catch (_: IllegalArgumentException) { }
                }
            }
        } finally { apk.delete() }
    }
}
