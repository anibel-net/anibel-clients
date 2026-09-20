package net.anibel.app

import android.app.Application
import java.io.File
import okio.Path.Companion.toOkioPath

class AnibelApplication : Application(), coil3.SingletonImageLoader.Factory {
    internal val episodeArtwork = EpisodeArtworkCache()
    override fun newImageLoader(context: android.content.Context): coil3.ImageLoader =
        coil3.ImageLoader.Builder(context)
            .memoryCache { coil3.memory.MemoryCache.Builder().maxSizeBytes(64L * 1024 * 1024).build() }
            .diskCache { coil3.disk.DiskCache.Builder()
                .directory(File(context.cacheDir, "image_cache").toOkioPath())
                .maxSizeBytes(256L * 1024 * 1024).build() }
            .build()
    override fun onCreate() { super.onCreate(); Language.initialize(this) }
    // One core and cache for the lifetime of this app process.
    internal val core by lazy { CoreClient(File(filesDir, "core"), CredentialStore(filesDir)) }
}
