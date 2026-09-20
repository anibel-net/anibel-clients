package net.anibel.app

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.clickable
import androidx.compose.ui.res.painterResource
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import org.json.JSONArray
import org.json.JSONObject
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.withContext
import java.io.File

internal enum class PersonalList(val key: String, val labelId: Int, val counter: String = key) {
    Favorites("favorites", R.string.favorites), InProgress("inprogress", R.string.in_progress, "inProgress"),
    Done("done", R.string.completed), Planned("planned", R.string.planned), Dropped("dropped", R.string.dropped);
    val label: String get() = ui(labelId)
}

@Composable
internal fun ProfileScreen(isTv: Boolean, username: String? = null, edgeToEdge: Boolean = false) {
    val session = LocalSession.current
    val core = LocalCore.current
    val navigator = LocalNavigator.current
    val signedIn = session.optBoolean("authenticated")
    var revision by remember { mutableIntStateOf(0) }
    val (action, run) = rememberAction()
    val profile = if (signedIn || username != null) coreData(CoreCommand.Profile, json("username" to username), revision) else null
    if (profile == RemoteData.Loading) {
        LoadingContent(Modifier.fillMaxSize())
        return
    }
    PageColumn(onRefresh = if (isTv) null else { { revision++ } },
        refreshing = revision > 0 && profile == RemoteData.Loading,
        header = {
                val user = ((profile as? RemoteData.Ready)?.value as? JSONObject)?.optJSONObject("profile")
                if (user != null) ProfileCover(user, edgeToEdge, isTv)
                else if (edgeToEdge) Spacer(Modifier.windowInsetsTopHeight(WindowInsets.statusBars))
        }) {
        if (!signedIn && username == null) {
            var login by rememberSaveable { mutableStateOf("") }
            // Password is not saved in instance state or persistent preferences.
            var password by remember { mutableStateOf("") }
            if (!isTv) Text(ui(R.string.profile), style = MaterialTheme.typography.headlineMedium)
            AppText(ui(R.string.sign_in_hint), isTv)
            OutlinedTextField(login, { login = it }, Modifier.fillMaxWidth(), label = { Text(ui(R.string.username)) }, singleLine = true, enabled = !action.busy)
            OutlinedTextField(password, { password = it }, Modifier.fillMaxWidth(), label = { Text(ui(R.string.password)) },
                visualTransformation = PasswordVisualTransformation(), singleLine = true, enabled = !action.busy)
            AppButton(if (action.busy) ui(R.string.signing_in) else ui(R.string.sign_in), isTv, {
                run { core.value(CoreCommand.Login, json("username" to login.trim(), "password" to password)); password = ""; revision++ }
            }, modifier = Modifier.fillMaxWidth(), enabled = !action.busy && login.isNotBlank() && password.isNotEmpty())
        } else {
            DataContent(checkNotNull(profile), isTv, { revision++ }) { value ->
                val view = value as? JSONObject
                val user = view?.optJSONObject("profile")
                if (user == null) AppText(ui(R.string.profile_missing), isTv)
                else {
                    if (view.optBoolean("isOwn")) {
                        AppButton(ui(R.string.edit_profile), isTv, { navigator.open(Screen.EditProfile, user) })
                    }
                }
            }
            if (username == null && signedIn && isTv) {
                AppButton(ui(R.string.sign_out), true, { run { core.value(CoreCommand.Logout); revision++ } }, enabled = !action.busy)
            }
        }
        ActionStatus(action, isTv)
        if (username == null && !isTv) {
            HorizontalDivider()
            Column {
                ListItem(headlineContent = { Text(ui(R.string.downloads)) },
                    leadingContent = { Icon(painterResource(R.drawable.ic_download), null) },
                    modifier = Modifier.clickable { navigator.open(Screen.Downloads, JSONObject()) })
                ListItem(headlineContent = { Text(ui(R.string.settings)) },
                    leadingContent = { Icon(painterResource(R.drawable.ic_settings), null) },
                    modifier = Modifier.clickable { navigator.open(Screen.Settings, JSONObject()) })
                if (signedIn) {
                    HorizontalDivider(Modifier.padding(vertical = 8.dp))
                    ListItem(headlineContent = { Text(ui(R.string.sign_out), color = MaterialTheme.colorScheme.error) },
                        modifier = Modifier.clickable(enabled = !action.busy) { run { core.value(CoreCommand.Logout); revision++ } })
                }
            }
        }
    }
}

@Composable
internal fun ProfileEditor(profile: JSONObject, isTv: Boolean, saved: () -> Unit) {
    var name by rememberSaveable { mutableStateOf(profile.text("displayName").orEmpty()) }
    var bio by rememberSaveable { mutableStateOf(profile.text("bio").orEmpty()) }
    val core = LocalCore.current
    val (action, run) = rememberAction()
    val context = LocalContext.current
    var imageField by rememberSaveable { mutableStateOf("avatarPath") }
    var avatar by rememberSaveable { mutableStateOf<String?>(null) }
    var wallpaper by rememberSaveable { mutableStateOf<String?>(null) }
    val picker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        if (uri != null) run {
            val path = withContext(Dispatchers.IO) {
                val file = File.createTempFile("profile-", ".image", context.cacheDir)
                try {
                    checkNotNull(context.contentResolver.openInputStream(uri)).use { input ->
                        file.outputStream().use { output ->
                            val buffer = ByteArray(8192)
                            var total = 0L
                            while (true) {
                                val count = input.read(buffer)
                                if (count < 0) break
                                total += count
                                require(total <= 10 * 1024 * 1024) { ui(R.string.image_too_large) }
                                output.write(buffer, 0, count)
                            }
                        }
                    }
                    file.path
                } catch (failure: Exception) { file.delete(); throw failure }
            }
            if (imageField == "avatarPath") { avatar?.let { File(it).delete() }; avatar = path }
            else { wallpaper?.let { File(it).delete() }; wallpaper = path }
        }
    }
    OutlinedTextField(name, { if (it.length <= 100) name = it }, Modifier.fillMaxWidth(), label = { Text(ui(R.string.display_name)) }, enabled = !action.busy)
    OutlinedTextField(bio, { if (it.length <= 2000) bio = it }, Modifier.fillMaxWidth(), label = { Text(ui(R.string.about_you)) }, minLines = 3, enabled = !action.busy)
    FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        AppButton(ui(R.string.choose_avatar), isTv, { imageField = "avatarPath"; picker.launch("image/*") }, enabled = !action.busy)
        AppButton(ui(R.string.choose_cover), isTv, { imageField = "wallpaperPath"; picker.launch("image/*") }, enabled = !action.busy)
    }
    avatar?.let { AppImage(File(it), ui(R.string.selected_avatar), Modifier.size(80.dp)) }
    wallpaper?.let { AppImage(File(it), ui(R.string.selected_cover), Modifier.fillMaxWidth().height(100.dp)) }
    AppButton(ui(R.string.save_profile), isTv, { run {
        val patch = json("displayName" to name, "bio" to bio)
        avatar?.let { patch.put("avatarPath", it) }
        wallpaper?.let { patch.put("wallpaperPath", it) }
        withContext(NonCancellable) { core.value(CoreCommand.UpdateProfile, patch) }
        avatar?.let { File(it).delete() }; wallpaper?.let { File(it).delete() }
        saved()
    } }, enabled = !action.busy)
    ActionStatus(action, isTv)
}

@Composable
internal fun PersonalScreen(kind: String, isTv: Boolean, mediaType: String? = null, showHeader: Boolean = true) {
    val navigator = LocalNavigator.current
    if (!LocalSession.current.optBoolean("authenticated")) {
        PageColumn {
            AppText(ui(R.string.sign_in_saved), isTv)
            AppButton(ui(R.string.sign_in), isTv, { navigator.open(Screen.Profile, JSONObject()) })
        }
        return
    }
    var revision by remember { mutableIntStateOf(0) }
    val data = coreData(CoreCommand.PersonalList, json("kind" to kind), revision)
    Column(Modifier.fillMaxSize()) {
        if (isTv && showHeader) Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp), horizontalArrangement = Arrangement.SpaceBetween) {
            AppText(PersonalList.entries.firstOrNull { it.key == kind }?.label.orEmpty(), isTv, Modifier.padding(12.dp))
            AppButton(ui(R.string.refresh), isTv, { revision++ })
        }
        RefreshablePage(isTv, data == RemoteData.Loading && revision > 0, { revision++ }, Modifier.weight(1f)) {
            DataContent(data, isTv, { revision++ }, loadingModifier = Modifier.fillMaxSize()) { value ->
                val titles = (value as? JSONArray).objects().filter { mediaType == null || it.optString("mediaType") == mediaType }
                TitleGrid(JSONArray(titles), isTv)
            }
        }
    }
}
