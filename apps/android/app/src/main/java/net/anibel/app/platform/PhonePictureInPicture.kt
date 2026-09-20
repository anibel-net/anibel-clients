package net.anibel.app

import android.app.PictureInPictureParams
import android.graphics.Rect
import android.os.Build
import android.util.Rational
import android.view.View
import androidx.activity.ComponentActivity
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.core.app.PictureInPictureModeChangedInfo
import androidx.core.util.Consumer
import androidx.media3.common.Player
import androidx.media3.common.util.UnstableApi
import androidx.media3.session.MediaSession
import androidx.media3.ui.AspectRatioFrameLayout
import androidx.media3.ui.PlayerView

@androidx.annotation.OptIn(UnstableApi::class)
@Composable
internal fun PhonePictureInPicture(activity: ComponentActivity, view: PlayerView, player: Player): () -> Unit {
    fun params(): PictureInPictureParams {
        val video = player.videoSize
        val ratio = if (video.height > 0) video.width.toDouble() * video.pixelWidthHeightRatio / video.height else 16.0 / 9
        val builder = PictureInPictureParams.Builder()
            .setAspectRatio(Rational((ratio.coerceIn(1.0 / 2.39, 2.39) * 1000).toInt(), 1000))
        if (!activity.isInPictureInPictureMode) {
            val bounds = Rect()
            if (view.getGlobalVisibleRect(bounds)) builder.setSourceRectHint(bounds)
        }
        if (Build.VERSION.SDK_INT >= 31) builder.setAutoEnterEnabled(
            player.playWhenReady && player.playbackState in listOf(Player.STATE_READY, Player.STATE_BUFFERING))
        return builder.build()
    }
    val enter = { activity.enterPictureInPictureMode(params()); Unit }
    DisposableEffect(activity, view, player) {
        // MediaSession supplies Android's own play/pause controls in the PiP window.
        val session = MediaSession.Builder(activity, player).build()
        var fullScreenResizeMode = view.resizeMode
        val mode = Consumer<PictureInPictureModeChangedInfo> { info ->
            if (info.isInPictureInPictureMode) {
                fullScreenResizeMode = view.resizeMode
                view.hideController()
                view.useController = false
                view.resizeMode = AspectRatioFrameLayout.RESIZE_MODE_FIT
            } else {
                view.resizeMode = fullScreenResizeMode
                view.useController = true
                view.showController()
            }
        }
        val leave = Runnable { if (Build.VERSION.SDK_INT < 31 && player.isPlaying) enter() }
        val events = object : Player.Listener {
            override fun onEvents(player: Player, events: Player.Events) { activity.setPictureInPictureParams(params()) }
        }
        val layout = View.OnLayoutChangeListener { _, _, _, _, _, _, _, _, _ ->
            if (!activity.isInPictureInPictureMode) activity.setPictureInPictureParams(params())
        }
        activity.addOnPictureInPictureModeChangedListener(mode)
        activity.addOnUserLeaveHintListener(leave)
        player.addListener(events)
        view.addOnLayoutChangeListener(layout)
        activity.setPictureInPictureParams(params())
        onDispose {
            activity.removeOnPictureInPictureModeChangedListener(mode)
            activity.removeOnUserLeaveHintListener(leave)
            player.removeListener(events)
            view.removeOnLayoutChangeListener(layout)
            val cleared = PictureInPictureParams.Builder().setActions(emptyList())
            if (Build.VERSION.SDK_INT >= 31) cleared.setAutoEnterEnabled(false)
            activity.setPictureInPictureParams(cleared.build())
            session.release()
        }
    }
    return enter
}
