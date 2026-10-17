package net.anibel.app

import android.view.ContextThemeWrapper
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import androidx.compose.runtime.*
import androidx.compose.ui.platform.ComposeView
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalView
import androidx.lifecycle.findViewTreeLifecycleOwner
import androidx.lifecycle.setViewTreeLifecycleOwner
import androidx.savedstate.findViewTreeSavedStateRegistryOwner
import androidx.savedstate.setViewTreeSavedStateRegistryOwner
import com.google.android.material.color.DynamicColors
import com.google.android.material.sidesheet.SideSheetDialog
import kotlin.math.roundToInt

@Composable
internal fun MaterialSideSheet(onDismiss: () -> Unit, content: @Composable () -> Unit) {
    val context = LocalContext.current
    val host = LocalView.current
    val parent = rememberCompositionContext()
    val dismiss by rememberUpdatedState(onDismiss)
    val sheetContent by rememberUpdatedState(content)
    DisposableEffect(context, host, parent) {
        val themed = DynamicColors.wrapContextIfAvailable(
            ContextThemeWrapper(context, com.google.android.material.R.style.Theme_Material3_DayNight_NoActionBar))
        val dialog = SideSheetDialog(themed)
        val view = ComposeView(dialog.context).apply {
            id = R.id.filters_side_sheet
            setViewTreeLifecycleOwner(host.findViewTreeLifecycleOwner())
            setViewTreeSavedStateRegistryOwner(host.findViewTreeSavedStateRegistryOwner())
            setParentCompositionContext(parent)
            layoutParams = ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT)
            setContent { sheetContent() }
        }
        dialog.setContentView(view)
        dialog.setSheetEdge(Gravity.END)
        val sheet = requireNotNull(dialog.findViewById<View>(com.google.android.material.R.id.m3_side_sheet))
        sheet.layoutParams = sheet.layoutParams.apply {
            width = (360 * themed.resources.displayMetrics.density).roundToInt()
                .coerceAtMost(themed.resources.displayMetrics.widthPixels)
        }
        dialog.setOnDismissListener { dismiss() }
        dialog.show()
        onDispose {
            dialog.setOnDismissListener(null)
            dialog.dismiss()
            view.disposeComposition()
        }
    }
}
