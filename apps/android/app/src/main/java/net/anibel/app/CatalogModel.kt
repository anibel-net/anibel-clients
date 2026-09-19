package net.anibel.app

import android.app.Application
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import org.json.JSONArray
import org.json.JSONObject

internal enum class FilterKey(val wire: String, val source: String, val labelId: Int) {
    Type("type", "types", R.string.filter_type), Year("year", "years", R.string.filter_year),
    Genre("genres", "genres", R.string.filter_genre), Studio("studies", "studios", R.string.filter_studio),
    Translator("translators", "translators", R.string.filter_translator),
    Editor("editors", "editors", R.string.filter_editor), Cleaner("cleanners", "cleanners", R.string.filter_cleaner),
    Typesetter("typpers", "typpers", R.string.filter_typesetter), Programmer("programmers", "programmers", R.string.filter_programmer),
    Dubber("dubbers", "dubbers", R.string.filter_dubber), AudioEngineer("audioEngineers", "audioEngineers", R.string.filter_audio),
    Language("language", "", R.string.language), Status("status", "", R.string.filter_status), Country("country", "", R.string.filter_country);

    val label: String get() = ui(labelId)
    val single: Boolean get() = this == Status || this == Country
}

internal class CatalogTitle(val data: JSONObject) {
    val id: String get() = data.optString("mediaId")
    val type: String get() = data.optString("mediaType")
    val slug: String get() = data.optString("slug")
    val title: String get() = data.localized("title").ifBlank { slug }
    val poster: String? get() = data.text("poster")
    val yearAndRating: String get() = listOfNotNull(data.text("year"),
        data.optDouble("rating").takeIf { it.isFinite() && it > 0 }?.let { "★ %.1f".format(it / 2) }).joinToString(" · ")
    val meta: String get() = listOf(keyLabel(type), yearAndRating).filter(String::isNotBlank).joinToString(" · ")
    val info: String get() = listOfNotNull(data.text("status")?.let(::keyLabel),
        data.text("updateType")?.takeIf { it == "sub" || it == "dub" }?.let(::keyLabel) ?: data.optJSONArray("language")?.strings()?.joinToString(" / ", transform = ::keyLabel),
        data.text("num")?.let { ui(if (type == "manga" || type == "books") R.string.chapter_number else R.string.episode_number, it) }).filter(String::isNotBlank).joinToString(" · ")
}
internal enum class LoadState { Loading, Ready, Failed }

internal class CatalogModel(application: Application) : AndroidViewModel(application) {
    private val core = (application as AnibelApplication).core
    private var catalog: AppPage? = null
    private var listJob: Job? = null
    private var filterJob: Job? = null
    private var nextOffset = 0L
    var titles by mutableStateOf<List<CatalogTitle>>(emptyList()); private set
    var total by mutableLongStateOf(0L); private set
    var hasMore by mutableStateOf(false); private set
    var state by mutableStateOf(LoadState.Loading); private set
    var error by mutableStateOf(""); private set
    var filterState by mutableStateOf(LoadState.Loading); private set
    var filterError by mutableStateOf(""); private set
    var options by mutableStateOf<Map<FilterKey, List<String>>>(emptyMap()); private set
    var selected by mutableStateOf<Map<FilterKey, Set<String>>>(emptyMap()); private set

    fun open(page: AppPage) {
        if (catalog != page) {
            stop()
            catalog = page
            selected = emptyMap()
            options = emptyMap()
            titles = emptyList()
            total = 0
            hasMore = false
            loadFilters()
            load(reset = true)
        } else {
            if (filterState == LoadState.Loading) loadFilters()
            if (state == LoadState.Loading) load(reset = titles.isEmpty())
        }
    }

    fun stop() { listJob?.cancel(); filterJob?.cancel() }

    fun toggle(key: FilterKey, value: String) {
        val old = selected[key].orEmpty()
        val values = when {
            value in old -> old - value
            key.single -> setOf(value)
            else -> old + value
        }
        selected = if (values.isEmpty()) selected - key else selected + (key to values)
        load(reset = true)
    }

    fun clear() { selected = emptyMap(); load(reset = true) }

    fun loadFilters() {
        val page = catalog ?: return
        filterJob?.cancel()
        filterState = LoadState.Loading
        filterJob = viewModelScope.launch {
            try {
                val data = core.call("filters", JSONObject().put("mediaType", page.mediaType), reload = true)
                options = buildMap {
                    FilterKey.entries.forEach { key ->
                        val video = page == AppPage.Anime || page == AppPage.Cinema
                        val values = when {
                            key == FilterKey.Studio && page != AppPage.Anime -> emptyList()
                            key == FilterKey.Type && page == AppPage.Games -> emptyList()
                            key == FilterKey.Dubber && !video -> emptyList()
                            key == FilterKey.Language -> if (video) listOf("sub", "dub") else emptyList()
                            key == FilterKey.Status -> if (page != AppPage.Games) listOf("ongoing", "finished") else emptyList()
                            key == FilterKey.Country -> if (page == AppPage.Anime) listOf("china") else emptyList()
                            else -> data.optJSONArray(key.source).strings()
                        }.filter(String::isNotBlank).distinct()
                        if (values.isNotEmpty()) put(key, if (key == FilterKey.Year) values.sortedByDescending { it.toLongOrNull() } else values)
                    }
                }
                filterState = LoadState.Ready
            } catch (cancelled: CancellationException) { throw cancelled
            } catch (failure: Exception) {
                filterError = failure.message ?: ui(R.string.load_filters_failed)
                filterState = LoadState.Failed
            }
        }
    }

    fun load(reset: Boolean = false, reload: Boolean = false) {
        val page = catalog ?: return
        if (!reset && listJob?.isActive == true) return
        listJob?.cancel()
        if (reset) { titles = emptyList(); total = 0; nextOffset = 0; hasMore = false }
        state = LoadState.Loading
        val offset = nextOffset
        val filters = JSONObject().apply {
            selected.forEach { (key, values) ->
                put(key.wire, when {
                    key.single -> values.first()
                    key == FilterKey.Year -> JSONArray(values.map(String::toLong))
                    else -> JSONArray(values.toList())
                })
            }
        }
        listJob = viewModelScope.launch {
            try {
                val result = core.call("mediaList", JSONObject().put("mediaType", page.mediaType)
                    .put("offset", offset).put("limit", 30).put("filters", filters), reload)
                val docs = result.getJSONArray("docs")
                val fetched = docs.objects().map(::CatalogTitle)
                titles = (titles + fetched).distinctBy { it.id }
                total = result.getLong("totalDocs")
                nextOffset = result.getLong("nextOffset")
                hasMore = result.getBoolean("hasMore") && nextOffset > offset
                state = LoadState.Ready
            } catch (cancelled: CancellationException) { throw cancelled
            } catch (failure: Exception) {
                error = failure.message ?: ui(R.string.load_titles_failed)
                state = LoadState.Failed
            }
        }
    }
}

internal fun JSONObject.text(key: String): String? = if (isNull(key)) null else optString(key).takeIf(String::isNotBlank)
internal fun JSONArray?.strings(): List<String> = if (this == null) emptyList() else (0 until length()).map { get(it).toString() }
