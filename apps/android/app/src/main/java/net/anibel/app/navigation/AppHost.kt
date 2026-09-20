package net.anibel.app

import androidx.activity.compose.BackHandler
import androidx.compose.animation.*
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.saveable.rememberSaveableStateHolder
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.launch
import org.json.JSONArray
import org.json.JSONObject

internal enum class Screen(val labelId: Int) {
    EditProfile(R.string.edit_profile), Collection(R.string.title), Detail(R.string.title), Search(R.string.search), Profile(R.string.profile), Personal(R.string.my_list),
    Downloads(R.string.downloads), Settings(R.string.settings), Reader(R.string.reader), Player(R.string.player);
    val label: String get() = ui(labelId)
}
internal class Navigator(val open: (Screen, JSONObject) -> Unit)
internal val LocalNavigator = staticCompositionLocalOf { Navigator { _, _ -> } }
internal val LocalSession = staticCompositionLocalOf { JSONObject() }
internal val LocalCore = staticCompositionLocalOf<CoreClient> { error("Missing core") }

internal fun json(vararg fields: Pair<String, Any?>) = JSONObject().apply {
    fields.forEach { (key, value) -> put(key, value ?: JSONObject.NULL) }
}
internal fun JSONArray?.objects(): List<JSONObject> = if (this == null) emptyList() else
    (0 until length()).mapNotNull { optJSONObject(it) }
internal fun JSONObject.localized(key: String): String = optJSONObject(key)?.let { value ->
    (if (java.util.Locale.getDefault().language == "en") listOf("en", "be", "ru") else listOf("be", "ru", "en")).firstNotNullOfOrNull(value::text)
}.orEmpty()

@OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)
@Composable
internal fun AppHost(isTv: Boolean, content: @Composable () -> Unit) {
    val application = LocalContext.current.applicationContext as AnibelApplication
    val core = application.core
    var stack by rememberSaveable { mutableStateOf(listOf<String>()) }
    val savedPages = rememberSaveableStateHolder()
    val currentStack = rememberUpdatedState(stack)
    var session by remember { mutableStateOf(JSONObject()) }
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    LaunchedEffect(core, lifecycle) {
        lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            try {
                if (core.call(CoreCommand.Downloads).optJSONArray("items").objects().any { it.optString("status") in listOf("queued", "downloading") })
                    application.startForegroundService(android.content.Intent(application, DownloadService::class.java))
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (_: Exception) { /* Downloads exposes a retry action. */ }
            core.snapshots.session.state.collect { result ->
                result?.getOrNull()?.let { next ->
                    if (next.toString() != session.toString()) session = next
                }
            }
        }
    }
    val navigator = remember { Navigator { screen, args ->
        stack = stack + json("screen" to screen.name, "args" to args).toString()
    } }
    BackHandler(stack.isNotEmpty()) { stack = stack.dropLast(1) }
    SystemTheme(isTv) {
        CompositionLocalProvider(LocalCore provides core, LocalNavigator provides navigator, LocalSession provides session) {
            AnimatedContent(targetState = stack, label = "navigation", transitionSpec = {
                val duration = if (isTv) 140 else 220
                val forward = targetState.size > initialState.size
                (fadeIn(tween(duration)) + slideInHorizontally(tween(duration)) { if (isTv) 0 else if (forward) it / 12 else -it / 12 }) togetherWith
                    (fadeOut(tween(duration)) + slideOutHorizontally(tween(duration)) { if (isTv) 0 else if (forward) -it / 12 else it / 12 })
            }) { shownStack ->
            if (shownStack.isEmpty()) savedPages.SaveableStateProvider("root") { content() }
            else savedPages.SaveableStateProvider("${shownStack.lastIndex}:${shownStack.last()}") {
                val route = JSONObject(shownStack.last())
                val screen = Screen.valueOf(route.getString("screen"))
                val args = route.getJSONObject("args")
                if (screen == Screen.EditProfile) DisposableEffect(shownStack.lastIndex, shownStack.last()) {
                    val index = shownStack.lastIndex
                    val entry = shownStack.last()
                    onDispose {
                        if (currentStack.value.getOrNull(index) != entry) savedPages.removeState("$index:$entry")
                    }
                }
                if (screen == Screen.Player || screen == Screen.Reader && !isTv) {
                    Box(Modifier.fillMaxSize().testTag("screen_${screen.name}")) {
                        if (screen == Screen.Player) PlayerScreen(args, isTv) { stack = stack.dropLast(1) }
                        else ReaderScreen(args, false) { stack = stack.dropLast(1) }
                    }
                    return@SaveableStateProvider
                }
                val background = if (isTv) androidx.tv.material3.MaterialTheme.colorScheme.background else MaterialTheme.colorScheme.background
                Column(Modifier.fillMaxSize().background(background).safeDrawingPadding().testTag("screen_${screen.name}")) {
                    if (isTv) Row(Modifier.fillMaxWidth().padding(horizontal = 12.dp),
                        verticalAlignment = androidx.compose.ui.Alignment.CenterVertically) {
                        AppButton(ui(R.string.back), true, { stack = stack.dropLast(1) }, Modifier.testTag("back"))
                        if (screen != Screen.Detail && screen != Screen.Search) AppText(screen.label, true, Modifier.padding(horizontal = 12.dp))
                        if (screen == Screen.Detail) {
                            Spacer(Modifier.weight(1f))
                            TitleOptions(args, null, isTv = true)
                        }
                    } else if (screen != Screen.Search && screen != Screen.Detail) androidx.compose.material3.TopAppBar(
                        title = { if (screen != Screen.Detail && screen != Screen.Search) androidx.compose.material3.Text(args.text("title") ?: screen.label) },
                        navigationIcon = {
                            androidx.compose.material3.IconButton({ stack = stack.dropLast(1) }, Modifier.testTag("back")) {
                                androidx.compose.material3.Icon(androidx.compose.ui.res.painterResource(R.drawable.ic_back), ui(R.string.back))
                            }
                        }, windowInsets = WindowInsets(0, 0, 0, 0))
                    when (screen) {
                        Screen.Collection -> if (args.optString("op") == "updatesPage")
                            PagedTitles(CoreCommand.UpdatesPage, json("type" to args.optString("type", "ALL")), isTv)
                        else TitleGrid(args.optJSONArray("items"), isTv)
                        Screen.Detail -> DetailScreen(args, isTv) { stack = stack.dropLast(1) }
                        Screen.Search -> SearchScreen(isTv) { stack = stack.dropLast(1) }
                        Screen.EditProfile -> Box(Modifier.weight(1f).imePadding()) {
                            PageColumn { ProfileEditor(args, isTv) { stack = stack.dropLast(1) } }
                        }
                        Screen.Profile -> ProfileScreen(isTv, args.text("username"))
                        Screen.Personal -> PersonalScreen(args.optString("kind", "favorites"), isTv)
                        Screen.Downloads -> DownloadsScreen(isTv)
                        Screen.Settings -> SettingsScreen(isTv)
                        Screen.Reader -> ReaderScreen(args, isTv)
                        Screen.Player -> PlayerScreen(args, isTv)
                    }
                }
            }
            }
        }
    }
}

internal sealed interface RemoteData {
    data object Loading : RemoteData
    data class Ready(val value: Any?) : RemoteData
    data class Failed(val message: String) : RemoteData
}

@Composable
internal fun coreData(op: CoreCommand, args: JSONObject = JSONObject(), revision: Int = 0): RemoteData {
    val core = LocalCore.current
    val session = LocalSession.current.optLong("revision")
    val encoded = args.toString()
    return produceState<RemoteData>(RemoteData.Loading, op, encoded, "$revision:$session") {
        value = RemoteData.Loading
        value = try { RemoteData.Ready(core.value(op, JSONObject(encoded), reload = revision > 0)) }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (error: Exception) { RemoteData.Failed(error.message ?: ui(R.string.load_page_failed)) }
    }.value
}

@Composable
internal fun DataContent(data: RemoteData, isTv: Boolean, retry: () -> Unit, loadingModifier: Modifier = Modifier, content: @Composable (Any?) -> Unit) {
    when (data) {
        RemoteData.Loading -> LoadingContent(loadingModifier)
        is RemoteData.Failed -> Column(Modifier.padding(16.dp)) {
            AppText(data.message, isTv)
            AppButton(ui(R.string.try_again), isTv, retry)
        }
        is RemoteData.Ready -> content(data.value)
    }
}

internal class PageAction {
    var busy by mutableStateOf(false)
    var message by mutableStateOf("")
}
@Composable
internal fun rememberAction(): Pair<PageAction, (suspend () -> Unit) -> Unit> {
    val state = remember { PageAction() }
    val scope = rememberCoroutineScope()
    val run: (suspend () -> Unit) -> Unit = { action ->
        if (!state.busy) {
            state.busy = true
            state.message = ""
            scope.launch {
                try { action() }
                catch (cancelled: CancellationException) { throw cancelled }
                catch (error: Exception) { state.message = error.message ?: ui(R.string.action_failed) }
                finally { state.busy = false }
            }
        }
    }
    return state to run
}

@Composable
internal fun PageColumn(onRefresh: (() -> Unit)? = null, refreshing: Boolean = false, header: @Composable () -> Unit = {}, content: @Composable ColumnScope.() -> Unit) {
    RefreshablePage(onRefresh == null, refreshing, { onRefresh?.invoke() }, Modifier.fillMaxSize()) {
    LazyColumn(Modifier.fillMaxSize()) {
        item { header() }
        item { Column(Modifier.padding(24.dp), verticalArrangement = Arrangement.spacedBy(16.dp), content = content) }
    }
    }
}
