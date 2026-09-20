package net.anibel.app

import android.content.Context
import android.util.AttributeSet
import android.view.MotionEvent
import android.view.ScaleGestureDetector
import androidx.media3.common.util.UnstableApi
import androidx.media3.ui.AspectRatioFrameLayout
import androidx.media3.ui.PlayerView

@androidx.annotation.OptIn(UnstableApi::class)
class PhonePlayerView(context: Context, attrs: AttributeSet? = null) : PlayerView(context, attrs) {
    internal var onFillChange: ((Boolean) -> Unit)? = null
    private var pinching = false
    private var scale = 1f
    private var changed = false
    private val detector = ScaleGestureDetector(context, object : ScaleGestureDetector.SimpleOnScaleGestureListener() {
        override fun onScaleBegin(detector: ScaleGestureDetector): Boolean {
            pinching = true
            scale = 1f
            changed = false
            return true
        }

        override fun onScale(detector: ScaleGestureDetector): Boolean {
            scale *= detector.scaleFactor
            if (!changed && (scale > 1.12f || scale < 0.89f)) {
                setVideoFill(scale > 1f)
                changed = true
            }
            return true
        }
    }).apply { isQuickScaleEnabled = false }

    internal fun setVideoFill(fill: Boolean) {
        resizeMode = if (fill) AspectRatioFrameLayout.RESIZE_MODE_ZOOM else AspectRatioFrameLayout.RESIZE_MODE_FIT
        onFillChange?.invoke(fill)
    }

    override fun dispatchTouchEvent(event: MotionEvent): Boolean {
        val wasPinching = pinching
        detector.onTouchEvent(event)
        if (pinching || wasPinching) {
            // Cancel any button press or seek drag when a two-finger scale takes over.
            if (!wasPinching) MotionEvent.obtain(event).let { cancel ->
                cancel.action = MotionEvent.ACTION_CANCEL
                super.dispatchTouchEvent(cancel)
                cancel.recycle()
            }
            if (event.actionMasked == MotionEvent.ACTION_UP || event.actionMasked == MotionEvent.ACTION_CANCEL) pinching = false
            return true
        }
        return super.dispatchTouchEvent(event)
    }
}
