package net.anibel.app

import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.media3.common.*
import androidx.media3.common.util.UnstableApi
import androidx.media3.ui.compose.material3.buttons.PlayPauseButton
import androidx.media3.ui.compose.material3.buttons.SeekBackButton
import androidx.media3.ui.compose.material3.buttons.SeekForwardButton
import androidx.media3.ui.compose.state.rememberProgressStateWithTickInterval

private enum class PlayerMenu { Settings, Audio, Subtitles, Speed }

@androidx.annotation.OptIn(UnstableApi::class)
@Composable
internal fun MaterialPlayerControls(player: Player, fillScreen: Boolean, onToggleFill: () -> Unit, onPip: (() -> Unit)?, onBack: () -> Unit, hide: () -> Unit, hold: (Boolean) -> Unit) {
    var menu by remember { mutableStateOf<PlayerMenu?>(null) }
    var scrub by remember { mutableStateOf<Float?>(null) }
    var tracks by remember(player) { mutableStateOf(player.currentTracks) }
    var parameters by remember(player) { mutableStateOf(player.trackSelectionParameters) }
    var speed by remember(player) { mutableFloatStateOf(player.playbackParameters.speed) }
    var canSeek by remember(player) { mutableStateOf(player.isCommandAvailable(Player.COMMAND_SEEK_IN_CURRENT_MEDIA_ITEM)) }
    val progress = rememberProgressStateWithTickInterval(player, 250)
    DisposableEffect(player) {
        val listener = object : Player.Listener {
            override fun onEvents(player: Player, events: Player.Events) {
                tracks = player.currentTracks
                parameters = player.trackSelectionParameters
                speed = player.playbackParameters.speed
                canSeek = player.isCommandAvailable(Player.COMMAND_SEEK_IN_CURRENT_MEDIA_ITEM)
            }
        }
        player.addListener(listener)
        onDispose { player.removeListener(listener) }
    }
    DisposableEffect(menu, scrub != null) {
        hold(menu != null || scrub != null)
        onDispose { }
    }
    CompositionLocalProvider(LocalContentColor provides Color.White) {
        Box(Modifier.fillMaxSize().background(Color.Black.copy(alpha = 0.32f))
            .pointerInput(Unit) { detectTapGestures(onTap = { hide() }) }.displayCutoutPadding().padding(16.dp)) {
            Row(Modifier.align(Alignment.TopCenter).fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                FilledTonalIconButton(onBack, Modifier.testTag("player_back")) {
                    Icon(painterResource(R.drawable.ic_back), ui(R.string.back))
                }
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    if (onPip != null) FilledTonalIconButton(onPip, Modifier.testTag("player_pip")) {
                        Icon(painterResource(R.drawable.ic_pip), ui(R.string.player_pip))
                    }
                    FilledTonalIconButton({ onToggleFill(); hold(false) }, Modifier.testTag("player_fill")) {
                        Icon(painterResource(if (fillScreen) R.drawable.ic_fullscreen_exit else R.drawable.ic_fullscreen),
                            ui(if (fillScreen) R.string.player_fit else R.string.player_fill))
                    }
                    FilledTonalIconButton({ menu = PlayerMenu.Subtitles }, Modifier.testTag("player_subtitles")) {
                        Icon(painterResource(R.drawable.ic_subtitles), ui(R.string.choice_sub))
                    }
                    Box {
                        FilledTonalIconButton({ menu = PlayerMenu.Settings }, Modifier.testTag("player_settings")) {
                            Icon(painterResource(R.drawable.ic_settings), ui(R.string.settings))
                        }
                        DropdownMenu(menu == PlayerMenu.Settings, { menu = null }) {
                            DropdownMenuItem(text = { Text(ui(R.string.player_audio)) }, onClick = { menu = PlayerMenu.Audio })
                            DropdownMenuItem(text = { Text(ui(R.string.player_speed)) }, onClick = { menu = PlayerMenu.Speed })
                        }
                    }
                }
            }
            Row(Modifier.align(Alignment.Center), horizontalArrangement = Arrangement.spacedBy(32.dp), verticalAlignment = Alignment.CenterVertically) {
                SeekBackButton(player, Modifier.size(56.dp).testTag("player_seek_back"),
                    contentDescription = { ui(R.string.player_seek_back) }, onClick = { onClick(); hold(false) })
                PlayPauseButton(player, Modifier.size(72.dp).background(MaterialTheme.colorScheme.primaryContainer, CircleShape).testTag("player_play_pause"),
                    tint = MaterialTheme.colorScheme.onPrimaryContainer,
                    contentDescription = { ui(if (showPlay) R.string.play else R.string.player_pause) },
                    onClick = { onClick(); hold(false) })
                SeekForwardButton(player, Modifier.size(56.dp).testTag("player_seek_forward"),
                    contentDescription = { ui(R.string.player_seek_forward) }, onClick = { onClick(); hold(false) })
            }
            Surface(Modifier.align(Alignment.BottomCenter).fillMaxWidth(), shape = MaterialTheme.shapes.large,
                color = MaterialTheme.colorScheme.surfaceContainer.copy(alpha = 0.94f)) {
                Row(Modifier.padding(horizontal = 20.dp, vertical = 8.dp), verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(20.dp)) {
                    val duration = progress.durationMs.coerceAtLeast(0)
                    val position = if (duration > 0) (progress.currentPositionMs.toDouble() / duration).toFloat().coerceIn(0f, 1f) else 0f
                    val shownPosition = scrub?.let { (it.toDouble() * duration).toLong() } ?: progress.currentPositionMs
                    Text("${androidx.media3.common.util.Util.getStringForTime(shownPosition)} / ${androidx.media3.common.util.Util.getStringForTime(duration)}",
                        style = MaterialTheme.typography.labelMedium.copy(fontFeatureSettings = "tnum"), color = MaterialTheme.colorScheme.onSurface)
                    val seekLabel = ui(R.string.player_seek)
                    Slider(value = scrub ?: position, onValueChange = { scrub = it },
                        onValueChangeFinished = {
                            scrub?.let { if (duration > 0 && canSeek) player.seekTo((it.toDouble() * duration).toLong().coerceIn(0, duration)) }
                            scrub = null
                        }, enabled = duration > 0 && canSeek,
                        modifier = Modifier.weight(1f).testTag("player_seek").semantics { contentDescription = seekLabel })
                }
            }
        }
    }
    val active = menu
    if (active != null && active != PlayerMenu.Settings) AlertDialog(onDismissRequest = { menu = null },
        title = { Text(ui(when (active) {
            PlayerMenu.Audio -> R.string.player_audio
            PlayerMenu.Subtitles -> R.string.choice_sub
            else -> R.string.player_speed
        })) }, text = {
            Column(Modifier.heightIn(max = 220.dp).verticalScroll(rememberScrollState())) {
                if (active == PlayerMenu.Speed) listOf(0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f).forEach { value ->
                    PlayerOption("${value}×", speed == value) { player.setPlaybackSpeed(value); menu = null }
                } else {
                    val type = if (active == PlayerMenu.Audio) C.TRACK_TYPE_AUDIO else C.TRACK_TYPE_TEXT
                    val disabled = type in parameters.disabledTrackTypes
                    PlayerOption(ui(if (type == C.TRACK_TYPE_TEXT) R.string.player_off else R.string.player_auto),
                        if (type == C.TRACK_TYPE_TEXT) disabled else tracks.groups.filter { it.type == type }.none { parameters.overrides.containsKey(it.mediaTrackGroup) }) {
                        player.trackSelectionParameters = parameters.buildUpon().clearOverridesOfType(type)
                            .setTrackTypeDisabled(type, type == C.TRACK_TYPE_TEXT).build()
                        menu = null
                    }
                    tracks.groups.filter { it.type == type }.forEach { group ->
                        (0 until group.length).filter { group.isTrackSupported(it) }.forEach { index ->
                            val format = group.getTrackFormat(index)
                            val name = format.label?.takeIf { it.isNotBlank() } ?: format.language?.takeIf { it.isNotBlank() && it != "und" }
                                ?.let { java.util.Locale.forLanguageTag(it).displayLanguage } ?: ui(R.string.player_track, index + 1)
                            PlayerOption(name, !disabled && group.isTrackSelected(index)) {
                                player.trackSelectionParameters = parameters.buildUpon().setTrackTypeDisabled(type, false)
                                    .setOverrideForType(TrackSelectionOverride(group.mediaTrackGroup, index)).build()
                                menu = null
                            }
                        }
                    }
                }
            }
        }, confirmButton = { TextButton({ menu = null }) { Text(ui(R.string.cancel)) } })
}

@Composable
private fun PlayerOption(label: String, selected: Boolean, choose: () -> Unit) {
    ListItem(headlineContent = { Text(label) }, colors = ListItemDefaults.colors(containerColor = Color.Transparent), leadingContent = { RadioButton(selected, onClick = null) },
        modifier = Modifier.selectable(selected, role = Role.RadioButton, onClick = choose))
}
