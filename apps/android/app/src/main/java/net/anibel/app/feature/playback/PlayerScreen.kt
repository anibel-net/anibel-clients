package net.anibel.app

import io.github.peerless2012.ass.media.kt.withAssSupport
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
import androidx.media3.ui.PlayerView
import kotlinx.coroutines.*
import org.json.JSONObject

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
    val session = remember(core, isTv) { AndroidPlaybackSession(core, context.applicationContext, isTv) }
    var revision by remember { mutableIntStateOf(0) }
    val assHandler = session.assHandler
    val player = session.player
    val error = session.error
    val external = session.external
    val playbackState = session.playbackState
    val rendered = session.rendered
    LaunchedEffect(session, args.toString(), revision) { session.run(args) }
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
                    .onFailure { session.error = ui(R.string.no_browser) }
            }) }
        } else if (external != null) EmbeddedVideo(external!!, Modifier.fillMaxSize()) { session.error = it }
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
                this.player = engine; keepScreenOn = true; setShowBuffering(PlayerView.SHOW_BUFFERING_ALWAYS); setShowSubtitleButton(true); requestFocus()
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
