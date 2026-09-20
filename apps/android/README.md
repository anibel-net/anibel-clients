# Anibel.Net for Android

One Kotlin app supports phones, tablets, Android TV, and Google TV.

- `MobileActivity`: Home, Catalogs, Favorites, and Profile in a bottom bar.
  Catalogs has Anime, Manga, Cinema, Games, and Books tabs below the search bar.
  At 600 dp the bottom bar becomes a side rail. At 840 dp, title details place
  the summary and episodes/chapters side by side. The title grid adapts to width.
- `TvActivity`: Home, Anime, Manga, Cinema, Games, Books, Profile, Download,
  and Settings in a sidebar. Use the remote D-pad and select button.
- Phone and tablet controls use Material 3 Expressive; TV uses TV Material.
  Both layouts follow system light/dark mode and Android 12+ system colors.
  Earlier Android versions use the standard Material color schemes.
- One application ID (`net.anibel.app`), app module, and APK. No device flavors.
- Minimum Android 8.0 (API 26); compile API 37.2 and target API 37.

Catalogs show live titles, posters, available filters, counts, and pagination.
Kotlin calls the Rust Application through its protocol 2 C ABI and a small JNI
adapter. It uses the core's default endpoints and private app data folder.
Requests run off the UI thread. Cancellation is sent to the core. API filter
keys and server pagination cursors are preserved. Coil loads poster images.

The selected page and catalog survive activity recreation. Mobile Back returns
Home. TV Back returns focus to the sidebar before returning Home; Back from Home
in the sidebar exits the activity.

Home, search, title details, comments, personal lists, profile editing, downloads,
a manga reader, and a native Media3 player call the shared core. Sign-in tokens
are encrypted with Android Keystore. Download export uses the system folder picker.
Embedded sources use an in-app WebView, with a browser fallback button. Account writes need a signed-in account.

Belarusian is the default app language. Settings can switch to English. Android
13+ also exposes these languages in system app settings. The choice survives
app restarts. Catalog text uses the selected language when the API supplies it.
Material 3 Expressive currently uses the pinned 1.5.0-alpha28 library.
Shared application rules remain in the Rust core. See
[the core contract](../../docs/SHARED-CORE-SPEC.md).

## Build

Use Android Studio to open this directory, or run from this directory:

```powershell
.\gradlew.bat :app:assembleDebug :app:lintDebug
```

Use JDK 17 or a Gradle-compatible newer JDK. The build uses AGP 9.4 and Gradle 9.6. Android Studio supplies a JDK.
Set `ANDROID_HOME` to your SDK directory, or set `sdk.dir` in the ignored
`local.properties` file. Gradle downloads the pinned project dependencies.

Install NDK `29.0.14206865`, the pinned Rust toolchain, and cargo-ndk:

```powershell
rustup target add aarch64-linux-android x86_64-linux-android
cargo install cargo-ndk --locked
```

Gradle builds and packages the Rust library for ARM64 devices and x86_64
emulators. Android uses Rustls; other platforms retain native TLS.
The first build also downloads Rust dependencies. Keep `cargo` on PATH.

## Run

The local phone AVD uses Android 17 (API 37.2, 16 KB pages). The TV AVD uses
Android 16 (API 36), the newest available TV image.

Start an Android virtual device, then run:

```powershell
adb install -r app/build/outputs/apk/debug/app-debug.apk
adb shell am start -n net.anibel.app/.MobileActivity
```

On an Android TV emulator, launch `net.anibel.app/.TvActivity` instead.
For several connected devices, add `-s <device-serial>` to each adb command.

The app has separate mobile and TV launcher entries. TV support and touch input
are optional manifest features so the same APK can install on all three targets.


## Navigation checks

Build the test APK with `./gradlew :app:assembleDebugAndroidTest` (use
`gradlew.bat` on Windows). Install the app and test APK on each test device.
Run `net.anibel.app.MobileNavigationTest` on a phone or tablet and
`net.anibel.app.TvNavigationTest` on Android TV, using Android Studio or:

```powershell
adb -s <device-serial> install -r app/build/outputs/apk/androidTest/debug/app-debug-androidTest.apk
adb -s <device-serial> shell am instrument -w -e class net.anibel.app.MobileNavigationTest net.anibel.app.test/androidx.test.runner.AndroidJUnitRunner
```

Use the TV test class for a TV device. These checks cover tab selection,
catalog switching, Back, activity recreation, and remote-control focus.
`CoreIntegrationTest` checks JNI UTF-8 and cancellation, then makes live API
requests for all five catalogs, filters, and pagination. Run it explicitly with
the same instrumentation command; its live check requires internet access.

On the API 37.2 phone image, the current Espresso 3.7 test runner fails during
input setup (`InputManager.getInstance` is absent). Core integration tests run;
UI flows must currently be checked with emulator input. TV UI tests run on API 36.
The TV AVD enables hardware keyboard input: use arrow keys and Enter.

The catalog and Home updates load the next page as the grid approaches its end.
TV filters use a two-column D-pad layout. Sidebar focus selects the page; its
Profile, Downloads, and Settings entries stay at the bottom.

`LivePlaybackTest` requires an actual rendered video frame, not only a ready
player. `LiveReaderTest` opens a live chapter and checks that a page image decodes.
Run these on API 36 while the API 37.2 Espresso issue remains. Phone flows on
API 37.2 can also be checked directly with emulator input.

Compose BOM: `2026.09.00`. Material 3 Expressive: `1.5.0-alpha28`.
Phone search, page headers, navigation, and cards use standard Material components.

Phone and tablet content pages use Material pull-to-refresh. The manga reader
uses full-screen controls, saved page position, and scroll/LTR/RTL reading modes.
Video controls include Back, and SSA/ASS subtitles use the OpenGL overlay from
[libass-android](https://github.com/peerless2012/libass-android) (MIT), backed by
[libass](https://github.com/libass/libass) (ISC). The core supplies subtitle fonts.
`RefreshTest` checks that an empty list can refresh more than once.

Phone playback uses the Media3 Material 3 buttons inside the PlayerView controller
layout. Back, track selection, speed and seeking share the native controller
visibility and timeout. The PlayerView video/subtitle surface and libass path
remain in use. TV retains its existing controller layout.

Phone playback supports fit/fill by button or two-finger pinch, and Android picture-in-picture through the player button or Home while playing. PiP uses system media controls and restores the full player on return. Episode artwork reserves its aspect ratio while loading. Screenshot URLs share a bounded process cache; Coil keeps images in a 64 MiB memory cache and a 256 MiB disk cache. The phone launcher uses an adaptive icon.

## Ownership and local playback checks

See [ARCHITECTURE.md](ARCHITECTURE.md) for lifecycle owners. `LocalCoreLifecycleTest`
and `LocalPlaybackTest` use isolated storage and generated media, with no website
requests from the test core. Before the playback test, generate an eight-second
H.264/AAC MP4 named `app/build/playback-fixtures/video.mp4`. The Android workflow
contains the exact FFmpeg command. Build the test APK after generating this asset.
No fixture media is included in normal app builds.
