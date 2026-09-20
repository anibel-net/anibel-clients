package net.anibel.app

import io.github.peerless2012.ass.media.AssHandler
import io.github.peerless2012.ass.media.AssHandlerConfig
import io.github.peerless2012.ass.media.type.AssRenderType
import io.github.peerless2012.ass.media.kt.withAssSupport
import io.github.peerless2012.ass.media.kt.withAssMkvSupport
import io.github.peerless2012.ass.media.parser.AssSubtitleParserFactory
import android.net.Uri
import androidx.compose.foundation.layout.*
import androidx.compose.runtime.*
import androidx.media3.common.*
import androidx.media3.common.util.UnstableApi
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.exoplayer.DefaultRenderersFactory
import kotlinx.coroutines.*
import org.json.JSONObject
import java.io.File

/** Owns one native player, subtitle renderer and ordered core playback session. */
@androidx.annotation.OptIn(UnstableApi::class)
internal class AndroidPlaybackSession(
    private val core: CoreClient,
    private val context: android.content.Context,
    private val isTv: Boolean,
) {
    var assHandler by mutableStateOf<AssHandler?>(null)
        private set
    var player by mutableStateOf<ExoPlayer?>(null)
        private set
    var error by mutableStateOf("")
    var external by mutableStateOf<String?>(null)
        private set
    var playbackState by mutableIntStateOf(Player.STATE_IDLE)
        private set
    var rendered by mutableStateOf(false)
        private set
    suspend fun run(args: JSONObject) = withContext(Dispatchers.Main.immediate) {
        external = null
        playbackState = Player.STATE_IDLE
        error = ""
        rendered = false
        var active: ExoPlayer? = null
        var session: Long? = null
        var sequence = 0L
        try {
            // Finish opening so every allocated core session gets a close report.
            val opened = withContext(NonCancellable) { core.openPlayback(args) }
            session = opened.sessionId
            currentCoroutineContext().ensureActive()
            if (opened.source is PlaybackSource.Embed) {
                external = opened.source.pageUrl
                awaitCancellation()
            }
            val source = (opened.source as PlaybackSource.Video).videoUrl
            val uri = if (source.startsWith("/")) Uri.fromFile(File(source)) else Uri.parse(source)
            val subtitles = opened.subtitlePaths.mapIndexed { index, path ->
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
                File(opened.configDirectory, "fonts").listFiles().orEmpty()
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
                    override fun onPlaybackStateChanged(state: Int) { this@AndroidPlaybackSession.playbackState = state }
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
                    engine.playbackState == Player.STATE_READY && !ready -> PlaybackEvent.Ready
                    engine.playbackState == Player.STATE_ENDED && !ended -> PlaybackEvent.Ended
                    ready && !ended && android.os.SystemClock.elapsedRealtime() - lastReport >= 5000 -> PlaybackEvent.Position
                    else -> null
                }
                if (event != null) {
                    val response = core.reportPlayback(session, Math.addExact(sequence, 1L).also { sequence = it }, event,
                        engine.currentPosition.coerceAtLeast(0) / 1000.0, engine.duration.coerceAtLeast(0) / 1000.0)
                    if (event == PlaybackEvent.Ready) {
                        if (args.text("mediaId") != null && args.text("episodeId") != null &&
                            args.text("episodeType") in listOf("sub", "dub"))
                            core.call(CoreCommand.RememberEpisode, args)
                        ready = true
                        response?.let { engine.seekTo((it * 1000).toLong()) }
                        val tracks = engine.currentTracks.groups.flatMap { group -> (0 until group.length).map { group to it } }
                        fun facts(type: Int) = org.json.JSONArray(tracks.mapIndexedNotNull { index, (group, track) ->
                            if (group.type != type) null else group.getTrackFormat(track).let { format ->
                                json("id" to index + 1, "lang" to format.language.orEmpty(), "title" to format.label.orEmpty(), "fileName" to format.label.orEmpty())
                            }
                        })
                        val selection = core.call(CoreCommand.SelectTracks, json("audio" to facts(C.TRACK_TYPE_AUDIO),
                            "subtitles" to facts(C.TRACK_TYPE_TEXT), "preferDub" to opened.preferDub))
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
                    if (event == PlaybackEvent.Ended) ended = true
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
                runCatching { core.reportPlayback(id, Math.addExact(sequence, 1L), PlaybackEvent.Close, position, duration) }
            } }
        }
    }
}
