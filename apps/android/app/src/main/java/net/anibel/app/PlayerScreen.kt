package net.anibel.app

import io.github.peerless2012.ass.media.AssHandler
import io.github.peerless2012.ass.media.AssHandlerConfig
import io.github.peerless2012.ass.media.type.AssRenderType
import io.github.peerless2012.ass.media.kt.withAssSupport
import io.github.peerless2012.ass.media.kt.withAssMkvSupport
import io.github.peerless2012.ass.media.parser.AssSubtitleParserFactory
import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.background
import androidx.compose.ui.unit.dp
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.viewinterop.AndroidView
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.media3.common.*
import androidx.media3.common.util.UnstableApi
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.exoplayer.DefaultRenderersFactory
import androidx.media3.ui.PlayerView
import kotlinx.coroutines.*
import org.json.JSONObject
import java.io.File

@androidx.annotation.OptIn(UnstableApi::class)
@Composable
internal fun PlayerScreen(args: JSONObject, isTv: Boolean, onBack: (() -> Unit)? = null) {
    val core = LocalCore.current
    val context = LocalContext.current
    val activity = generateSequence(context) { (it as? android.content.ContextWrapper)?.baseContext }
        .filterIsInstance<android.app.Activity>().firstOrNull()
    DisposableEffect(activity, isTv) {
        val orientation = activity?.requestedOrientation
        val bars = activity?.let { androidx.core.view.WindowCompat.getInsetsController(it.window, it.window.decorView) }
        val behavior = bars?.systemBarsBehavior
        if (!isTv) activity?.requestedOrientation = android.content.pm.ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
        bars?.systemBarsBehavior = androidx.core.view.WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
        bars?.hide(androidx.core.view.WindowInsetsCompat.Type.systemBars())
        onDispose {
            if (!isTv && orientation != null) activity?.requestedOrientation = orientation
            bars?.show(androidx.core.view.WindowInsetsCompat.Type.systemBars())
            if (behavior != null) bars?.systemBarsBehavior = behavior
        }
    }
    val composition = rememberCompositionContext()
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    var assHandler by remember { mutableStateOf<AssHandler?>(null) }
    var player by remember { mutableStateOf<ExoPlayer?>(null) }
    var error by remember { mutableStateOf("") }
    var external by remember { mutableStateOf<String?>(null) }
    var revision by remember { mutableIntStateOf(0) }
    var playbackState by remember { mutableIntStateOf(Player.STATE_IDLE) }
    var rendered by remember { mutableStateOf(false) }
    LaunchedEffect(args.toString(), revision) { withContext(Dispatchers.Main.immediate) {
        external = null
        playbackState = Player.STATE_IDLE
        error = ""
        rendered = false
        var active: ExoPlayer? = null
        var session: Long? = null
        var sequence = 0L
        try {
            // Finish opening so every allocated core session gets a close report.
            val opened = withContext(NonCancellable) { core.call("playbackOpen", args) }
            session = opened.getLong("sessionId")
            currentCoroutineContext().ensureActive()
            val intent = opened.getJSONObject("intent")
            if (intent.optString("kind") == "embed") {
                external = intent.text("pageUrl")
                awaitCancellation()
            }
            val source = intent.getString("videoSrc")
            val uri = if (source.startsWith("/")) Uri.fromFile(File(source)) else Uri.parse(source)
            val subtitles = opened.optJSONArray("subtitlePaths").strings().mapIndexed { index, path ->
                MediaItem.SubtitleConfiguration.Builder(Uri.fromFile(File(path)))
                    .setMimeType(when (File(path).extension.lowercase()) { "srt" -> MimeTypes.APPLICATION_SUBRIP; "vtt" -> MimeTypes.TEXT_VTT; else -> MimeTypes.TEXT_SSA })
                    .setLabel(File(path).name).setId("external-$index").build()
            }
            val handler = AssHandler(AssRenderType.OVERLAY_OPEN_GL, AssHandlerConfig(maxRenderPixels = 1920 * 1080))
            assHandler = handler
            val parser = AssSubtitleParserFactory(handler)
            val sources = androidx.media3.exoplayer.source.DefaultMediaSourceFactory(
                androidx.media3.datasource.DefaultDataSource.Factory(context),
                androidx.media3.extractor.DefaultExtractorsFactory().withAssMkvSupport(parser, handler))
                .setSubtitleParserFactory(parser)
            val fonts = withContext(Dispatchers.IO) {
                File(opened.getString("configDirectory"), "fonts").listFiles().orEmpty()
                    .filter { it.isFile }.map { it.name to it.readBytes() }
            }
            fonts.forEach { (name, bytes) -> handler.ass.addFont(name, bytes) }
            active = ExoPlayer.Builder(context, DefaultRenderersFactory(context).setEnableDecoderFallback(true).withAssSupport(handler))
                .setMediaSourceFactory(sources).apply {
                    if (!isTv) { setSeekBackIncrementMs(10_000); setSeekForwardIncrementMs(10_000) }
                }.build().apply {
                handler.init(this)
                setAudioAttributes(AudioAttributes.Builder().setUsage(C.USAGE_MEDIA).setContentType(C.AUDIO_CONTENT_TYPE_MOVIE).build(), true)
                setHandleAudioBecomingNoisy(true)
                addListener(object : Player.Listener {
                    override fun onPlayerError(failure: PlaybackException) { error = failure.message ?: ui(R.string.playback_failed) }
                    override fun onPlaybackStateChanged(state: Int) { playbackState = state }
                    override fun onRenderedFirstFrame() { rendered = true }
                })
                if (!isTv) trackSelectionParameters = trackSelectionParameters.buildUpon()
                    .clearViewportSizeConstraints().setForceHighestSupportedBitrate(true).build()
                setMediaItem(MediaItem.Builder().setUri(uri).setSubtitleConfigurations(subtitles).build())
                prepare()
                playWhenReady = true
            }
            player = active
            var ready = false
            var ended = false
            var lastReport = 0L
            while (true) {
                val engine = active
                val event = when {
                    engine.playbackState == Player.STATE_READY && !ready -> "ready"
                    engine.playbackState == Player.STATE_ENDED && !ended -> "ended"
                    ready && !ended && android.os.SystemClock.elapsedRealtime() - lastReport >= 5000 -> "position"
                    else -> null
                }
                if (event != null) {
                    val response = core.call("playbackReport", json("sessionId" to session, "sequence" to ++sequence,
                        "event" to event, "position" to engine.currentPosition.coerceAtLeast(0) / 1000.0,
                        "duration" to engine.duration.coerceAtLeast(0) / 1000.0))
                    if (event == "ready") {
                        args.text("mediaId")?.let { mediaId ->
                            context.getSharedPreferences("playback", android.content.Context.MODE_PRIVATE).edit()
                                .putString("last:${args.optString("mediaType")}:$mediaId", args.toString()).apply()
                        }
                        ready = true
                        response.text("seekTo")?.toDoubleOrNull()?.let { engine.seekTo((it * 1000).toLong()) }
                        val tracks = engine.currentTracks.groups.flatMap { group -> (0 until group.length).map { group to it } }
                        fun facts(type: Int) = org.json.JSONArray(tracks.mapIndexedNotNull { index, (group, track) ->
                            if (group.type != type) null else group.getTrackFormat(track).let { format ->
                                json("id" to index + 1, "lang" to format.language.orEmpty(), "title" to format.label.orEmpty(), "fileName" to format.label.orEmpty())
                            }
                        })
                        val selection = core.call("selectTracks", json("audio" to facts(C.TRACK_TYPE_AUDIO),
                            "subtitles" to facts(C.TRACK_TYPE_TEXT), "preferDub" to opened.optBoolean("preferDub")))
                        val parameters = engine.trackSelectionParameters.buildUpon()
                        for ((key, type) in listOf("audio" to C.TRACK_TYPE_AUDIO, "subtitle" to C.TRACK_TYPE_TEXT)) {
                            val index = selection.optInt(key, 0) - 1
                            if (index in tracks.indices) {
                                val (group, track) = tracks[index]
                                parameters.setOverrideForType(TrackSelectionOverride(group.mediaTrackGroup, track))
                            }
                            if (type == C.TRACK_TYPE_TEXT) parameters.setTrackTypeDisabled(type, index !in tracks.indices)
                        }
                        engine.trackSelectionParameters = parameters.build()
                    }
                    if (event == "ended") ended = true
                    lastReport = android.os.SystemClock.elapsedRealtime()
                }
                delay(250)
            }
        } catch (cancelled: CancellationException) { throw cancelled }
        catch (failure: Exception) { error = failure.message ?: ui(R.string.cannot_play) }
        finally {
            active?.pause()
            val position = active?.currentPosition?.coerceAtLeast(0)?.div(1000.0) ?: 0.0
            val duration = active?.duration?.coerceAtLeast(0)?.div(1000.0) ?: 0.0
            active?.release(); player = null
            assHandler?.release(); assHandler = null
            session?.let { id -> withContext(NonCancellable) {
                runCatching { core.value("playbackReport", json("sessionId" to id, "sequence" to ++sequence,
                    "event" to "close", "position" to position, "duration" to duration)) }
            } }
        }
    }
    }
    DisposableEffect(lifecycle, player) {
        val observer = LifecycleEventObserver { _, event -> if (event == Lifecycle.Event.ON_STOP) player?.pause() }
        lifecycle.addObserver(observer)
        onDispose { lifecycle.removeObserver(observer) }
    }
    Box(Modifier.fillMaxSize().background(androidx.compose.ui.graphics.Color.Black)) {
        if (error.isNotBlank()) Column(Modifier.align(androidx.compose.ui.Alignment.Center).padding(48.dp),
            horizontalAlignment = androidx.compose.ui.Alignment.CenterHorizontally) {
            androidx.compose.material3.Text(error, Modifier.testTag("player_error"), color = androidx.compose.ui.graphics.Color.White)
            AppButton(ui(R.string.try_again), isTv, { revision++ })
            external?.let { url -> AppButton(ui(R.string.open_web_player), isTv, {
                runCatching { context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url))) }
                    .onFailure { error = ui(R.string.no_browser) }
            }) }
        } else if (external != null) EmbeddedVideo(external!!, Modifier.fillMaxSize()) { error = it }
        else if (player == null) LoadingSpinner(Modifier.align(androidx.compose.ui.Alignment.Center))
        else player?.let { engine ->
            AndroidView(factory = { viewContext ->
                (if (isTv) PlayerView(viewContext) else android.view.LayoutInflater.from(viewContext)
                    .inflate(R.layout.phone_player, null) as PlayerView).apply {
                id = R.id.native_player
                subtitleView?.let { subtitle -> assHandler?.let { subtitle.withAssSupport(it) } }
                if (isTv && onBack != null) findViewById<android.view.ViewGroup>(androidx.media3.ui.R.id.exo_basic_controls)?.let { controls ->
                    val back = android.widget.ImageButton(context).apply {
                        id = R.id.player_back
                        setImageResource(R.drawable.ic_back)
                        imageTintList = android.content.res.ColorStateList.valueOf(android.graphics.Color.WHITE)
                        contentDescription = ui(R.string.back)
                        val value = android.util.TypedValue()
                        context.theme.resolveAttribute(android.R.attr.selectableItemBackgroundBorderless, value, true)
                        setBackgroundResource(value.resourceId)
                        setOnClickListener { onBack() }
                    }
                    val size = (48 * resources.displayMetrics.density).toInt()
                    controls.addView(back, 0, android.view.ViewGroup.LayoutParams(size, size))
                }
                if (!isTv) {
                    setControllerAnimationEnabled(false)
                    val playerView = this
                    findViewById<androidx.compose.ui.platform.ComposeView>(R.id.material_player_controls).apply {
                        setParentCompositionContext(composition)
                        setContent {
                            var fillScreen by remember { mutableStateOf(false) }
                            DisposableEffect(playerView) {
                                (playerView as PhonePlayerView).onFillChange = { fillScreen = it }
                                onDispose { playerView.onFillChange = null }
                            }
                            val pip = (activity as? androidx.activity.ComponentActivity)?.takeIf {
                                it.packageManager.hasSystemFeature(android.content.pm.PackageManager.FEATURE_PICTURE_IN_PICTURE)
                            }?.let { PhonePictureInPicture(it, playerView, engine) }
                            SystemTheme {
                                MaterialPlayerControls(engine, fillScreen = fillScreen, onToggleFill = {
                                    (playerView as PhonePlayerView).setVideoFill(!fillScreen)
                                }, onPip = pip, onBack = { onBack?.invoke() },
                                    hide = { playerView.hideController() },
                                    hold = { held -> playerView.controllerShowTimeoutMs = if (held) 0 else 4000; playerView.showController() })
                            }
                        }
                    }
                }
                player = engine; keepScreenOn = true; setShowBuffering(PlayerView.SHOW_BUFFERING_ALWAYS); setShowSubtitleButton(true); requestFocus()
            } }, update = { it.player = engine },
                modifier = Modifier.fillMaxSize().testTag(if (playbackState == Player.STATE_READY && rendered) "player_ready" else "player_loading"),
                onRelease = { it.findViewById<androidx.compose.ui.platform.ComposeView>(R.id.material_player_controls)?.disposeComposition(); it.player = null })
        }
    if (onBack != null && player == null) androidx.compose.material3.FilledTonalIconButton(onBack,
        Modifier.align(androidx.compose.ui.Alignment.TopStart).displayCutoutPadding().padding(8.dp).testTag("back")) {
        androidx.compose.material3.Icon(androidx.compose.ui.res.painterResource(R.drawable.ic_back), ui(R.string.back))
    }
    }
}

@Composable
private fun EmbeddedVideo(url: String, modifier: Modifier, failed: (String) -> Unit) {
    val uri = Uri.parse(url)
    if (uri.scheme !in listOf("https", "http")) {
        LaunchedEffect(url) { failed(ui(R.string.cannot_play)) }
        return
    }
    var progress by remember(url) { mutableIntStateOf(0) }
    // Embedded sources need their web player. Keep them inside the app, including on TV.
    Box(modifier) {
    AndroidView(modifier = Modifier.fillMaxSize(), factory = { context ->
        android.webkit.WebView(context).apply {
            settings.javaScriptEnabled = true
            settings.domStorageEnabled = true
            settings.allowFileAccess = false
            settings.allowContentAccess = false
            settings.mediaPlaybackRequiresUserGesture = false
            webChromeClient = object : android.webkit.WebChromeClient() {
                override fun onProgressChanged(view: android.webkit.WebView, newProgress: Int) {
                    progress = newProgress.coerceIn(0, 100)
                }
            }
            webViewClient = object : android.webkit.WebViewClient() {
                override fun shouldOverrideUrlLoading(view: android.webkit.WebView, request: android.webkit.WebResourceRequest): Boolean =
                    request.url.scheme !in listOf("https", "http")
                override fun onReceivedError(view: android.webkit.WebView, request: android.webkit.WebResourceRequest, error: android.webkit.WebResourceError) {
                    if (request.isForMainFrame) failed(error.description.toString())
                }
            }
            loadUrl(url)
            requestFocus()
        }
    }, onRelease = { it.stopLoading(); it.destroy() })
    if (progress < 100) androidx.compose.material3.LinearProgressIndicator(
        progress = { progress / 100f }, modifier = Modifier.fillMaxWidth())
    }
}
