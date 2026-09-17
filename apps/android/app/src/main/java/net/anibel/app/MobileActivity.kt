package net.anibel.app

import android.os.Bundle
import android.graphics.Color
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp

class MobileActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge(
            statusBarStyle = SystemBarStyle.dark(Color.TRANSPARENT),
            navigationBarStyle = SystemBarStyle.dark(Color.TRANSPARENT),
        )
        setContent { MobileWelcome() }
    }
}

@Composable
internal fun MobileWelcome() {
    MaterialTheme(
        colorScheme = darkColorScheme(
            primary = AnibelAccent,
            background = AnibelBackground,
            surface = AnibelBackground,
            onBackground = AnibelText,
            onSurface = AnibelText,
        ),
    ) {
        Surface(Modifier.fillMaxSize()) {
            BoxWithConstraints(
                Modifier.fillMaxSize().safeDrawingPadding().padding(32.dp),
                contentAlignment = Alignment.Center,
            ) {
                // Use available window width, including split-screen windows.
                if (maxWidth >= 600.dp) {
                    Row(
                        Modifier.widthIn(max = 880.dp).verticalScroll(rememberScrollState()),
                        horizontalArrangement = Arrangement.spacedBy(48.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        BrandMark(Modifier.size(200.dp))
                        WelcomeText(Modifier.weight(1f))
                    }
                } else {
                    Column(
                        Modifier.widthIn(max = 440.dp).verticalScroll(rememberScrollState()),
                        verticalArrangement = Arrangement.spacedBy(32.dp),
                    ) {
                        BrandMark(Modifier.size(144.dp))
                        WelcomeText()
                    }
                }
            }
        }
    }
}

@Composable
private fun WelcomeText(modifier: Modifier = Modifier) {
    Column(modifier, verticalArrangement = Arrangement.spacedBy(16.dp)) {
        Text(
            stringResource(R.string.app_name),
            color = MaterialTheme.colorScheme.primary,
            style = MaterialTheme.typography.titleLarge,
        )
        Text(
            stringResource(R.string.welcome_title),
            style = MaterialTheme.typography.headlineLarge,
        )
        Text(
            stringResource(R.string.welcome_subtitle),
            style = MaterialTheme.typography.bodyLarge,
        )
    }
}

@Preview(name = "Phone", widthDp = 393, heightDp = 852)
@Preview(name = "Tablet", widthDp = 1024, heightDp = 768)
@Composable
private fun MobileWelcomePreview() {
    MobileWelcome()
}
