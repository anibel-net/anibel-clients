package net.anibel.app

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.tv.material3.MaterialTheme
import androidx.tv.material3.Text
import androidx.tv.material3.darkColorScheme

class TvActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent { TvWelcome() }
    }
}

@Composable
internal fun TvWelcome() {
    MaterialTheme(
        colorScheme = darkColorScheme(
            primary = AnibelAccent,
            background = AnibelBackground,
            onBackground = AnibelText,
        ),
    ) {
        Box(
            Modifier.fillMaxSize().background(AnibelBackground).padding(48.dp),
            contentAlignment = Alignment.Center,
        ) {
            Row(
                Modifier.widthIn(max = 960.dp),
                horizontalArrangement = Arrangement.spacedBy(48.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                BrandMark(Modifier.size(200.dp))
                Column(
                    Modifier.weight(1f),
                    verticalArrangement = Arrangement.spacedBy(20.dp),
                ) {
                    Text(
                        stringResource(R.string.app_name),
                        color = AnibelAccent,
                        style = MaterialTheme.typography.headlineSmall,
                    )
                    Text(
                        stringResource(R.string.welcome_title),
                        color = AnibelText,
                        style = MaterialTheme.typography.displaySmall,
                    )
                    Text(
                        stringResource(R.string.welcome_subtitle),
                        color = AnibelText,
                        style = MaterialTheme.typography.bodyLarge,
                    )
                }
            }
        }
    }
}

@Preview(name = "TV", widthDp = 960, heightDp = 540)
@Composable
private fun TvWelcomePreview() {
    TvWelcome()
}
