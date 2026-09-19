package net.anibel.app

import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import kotlinx.coroutines.runBlocking
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class CoreIntegrationTest {
    @Test fun credentialsAreEncryptedAndCanBeCleared() {
        val app = ApplicationProvider.getApplicationContext<AnibelApplication>()
        val folder = java.io.File(app.cacheDir, "credential-test").apply { mkdirs() }
        val store = CredentialStore(folder)
        try {
            store.write(json("token" to "test-secret-token", "username" to "test-user"))
            assertEquals("test-secret-token", store.read()?.getString("token"))
            assertFalse(java.io.File(folder, "credentials.bin").readBytes().toString(Charsets.UTF_8).contains("test-secret-token"))
            store.clear()
            assertNull(store.read())
        } finally { store.clear() }
    }

    @Test fun liveHomeAndTitlePages() = runBlocking {
        val core = ApplicationProvider.getApplicationContext<AnibelApplication>().core
        assertTrue((core.value("slider", json("limit" to 3)) as org.json.JSONArray).length() > 0)
        assertTrue(core.call("updatesPage", json("type" to "ALL", "offset" to 0, "limit" to 3)).getJSONArray("docs").length() > 0)
        assertNotNull(core.call("downloads").getJSONArray("items"))
        for (catalog in AppPage.catalogs) {
            val card = core.call("mediaList", json("mediaType" to catalog.mediaType, "offset" to 0, "limit" to 1)).getJSONArray("docs").getJSONObject(0)
            val detail = core.call("media", json("slug" to card.getString("slug"), "mediaType" to catalog.mediaType))
            assertEquals(card.getString("mediaId"), detail.getString("mediaId"))
            val kind = core.call("mediaKind", json("mediaType" to catalog.mediaType))
            when (kind.getString("content")) {
                "episodes" -> assertNotNull(core.call("episodeChoices", json("mediaId" to detail.getString("mediaId"), "kind" to "dub")).getJSONArray("items"))
                "chapters" -> assertNotNull(core.call("chapters", json("mediaId" to detail.getString("mediaId"), "limit" to 1000)).getJSONArray("docs"))
            }
            assertNotNull(core.call("comments", json("mediaId" to detail.getString("mediaId"), "mediaType" to catalog.mediaType, "offset" to 0, "limit" to 20)).getJSONArray("docs"))
        }
    }

    @Test fun abiPreservesUtf8AndCancellation() {
        val app = ApplicationProvider.getApplicationContext<AnibelApplication>()
        val folder = java.io.File(app.cacheDir, "мост-📚").apply { mkdirs() }
        val handle = CoreNative.init(JSONObject().put("dataDir", folder.path).toString().toByteArray())
        assertTrue(handle > 0)
        try {
            assertEquals(1, CoreNative.begin(handle, 41))
            CoreNative.cancel(handle, 41)
            val cancelled = JSONObject(String(CoreNative.call(handle, "{\"id\":41,\"op\":\"health\"}".toByteArray())!!))
            assertFalse(cancelled.getBoolean("ok"))
            assertEquals("cancelled", cancelled.getJSONObject("error").getString("code"))
            val unknown = JSONObject(String(CoreNative.call(handle, "{\"id\":42,\"op\":\"кнігі📚\"}".toByteArray())!!))
            assertFalse(unknown.getBoolean("ok"))
            assertTrue(unknown.getJSONObject("error").getString("message").contains("кнігі📚"))
            assertEquals(1, CoreNative.begin(handle, 41))
            CoreNative.call(handle, "{\"id\":41,\"op\":\"health\"}".toByteArray())
        } finally { CoreNative.shutdown(handle) }
    }

    // Explicit live smoke check: verifies Android TLS, the ABI, and all five API catalogs.
    @Test fun liveCatalogsFiltersAndPagination() = runBlocking {
        val core = ApplicationProvider.getApplicationContext<AnibelApplication>().core
        for (catalog in AppPage.catalogs) {
            val args = JSONObject().put("mediaType", catalog.mediaType).put("limit", 2).put("offset", 0)
            val first = core.call("mediaList", args, reload = true)
            assertTrue("${catalog.name} has titles", first.getJSONArray("docs").length() > 0)
            val title = first.getJSONArray("docs").getJSONObject(0)
            assertNotNull(title.getJSONObject("title").text("be") ?: title.getJSONObject("title").text("ru"))
            val filters = core.call("filters", JSONObject().put("mediaType", catalog.mediaType), reload = true)
            assertTrue("${catalog.name} has filter options", filters.length() > 0)
            if (first.getBoolean("hasMore")) {
                val second = core.call("mediaList", args.put("offset", first.getLong("nextOffset")))
                assertNotEquals(title.getString("mediaId"), second.getJSONArray("docs").getJSONObject(0).getString("mediaId"))
            }
            val year = filters.optJSONArray("years")?.optLong(0) ?: 0
            if (year > 0) {
                val filtered = core.call("mediaList", args.put("offset", 0)
                    .put("filters", JSONObject().put("year", org.json.JSONArray().put(year))), reload = true)
                val docs = filtered.getJSONArray("docs")
                for (index in 0 until docs.length()) assertEquals(year, docs.getJSONObject(index).getLong("year"))
            }
        }
    }
}
