package net.anibel.app

import androidx.annotation.DrawableRes
import androidx.annotation.StringRes

internal enum class AppPage(
    @get:StringRes val title: Int,
    @get:DrawableRes val icon: Int,
) {
    Home(R.string.nav_home, R.drawable.ic_home),
    Catalogs(R.string.nav_catalogs, R.drawable.ic_catalogs),
    Favorites(R.string.nav_favorites, R.drawable.ic_favorite),
    Profile(R.string.nav_profile, R.drawable.ic_profile),
    Download(R.string.nav_download, R.drawable.ic_download),
    Settings(R.string.nav_settings, R.drawable.ic_settings),
    Anime(R.string.catalog_anime, R.drawable.ic_play),
    Manga(R.string.catalog_manga, R.drawable.ic_book),
    Cinema(R.string.catalog_cinema, R.drawable.ic_play),
    Games(R.string.catalog_games, R.drawable.ic_games),
    Books(R.string.catalog_books, R.drawable.ic_book);

    val mediaType: String get() = name.lowercase(java.util.Locale.ROOT)

    companion object {
        val mobileNavigation = listOf(Home, Catalogs, Favorites, Profile)
        val tvNavigation = listOf(Home, Anime, Manga, Cinema, Games, Books, Favorites, Profile, Download, Settings)
        val catalogs = listOf(Anime, Manga, Cinema, Games, Books)
    }
}
