# Anibel.Net for Android

One Kotlin app supports phones, tablets, Android TV, and Google TV.

- `MobileActivity`: Jetpack Compose Material 3; layout follows window width.
- `TvActivity`: Compose for TV; a separate TV launcher and screen.
- One application ID (`net.anibel.app`), app module, and APK. No device flavors.
- Minimum Android 8.0 (API 26); compile and target API 36.

This is the initial project and welcome screen. Catalog, account, playback,
remote-control actions, and the Kotlin-to-Rust connection are not implemented.
Shared application rules must remain in the Rust core. See
[the core contract](../../docs/SHARED-CORE-SPEC.md).

## Build

Use Android Studio to open this directory, or run from this directory:

```powershell
.\gradlew.bat :app:assembleDebug :app:lintDebug
```

Use JDK 17 or a Gradle-compatible newer JDK. Android Studio supplies a JDK.
Set `ANDROID_HOME` to your SDK directory, or set `sdk.dir` in the ignored
`local.properties` file. Gradle downloads the pinned project dependencies.

## Run

Start an Android virtual device, then run:

```powershell
adb install -r app/build/outputs/apk/debug/app-debug.apk
adb shell am start -n net.anibel.app/.MobileActivity
```

On an Android TV emulator, launch `net.anibel.app/.TvActivity` instead.
For several connected devices, add `-s <device-serial>` to each adb command.

The app has separate mobile and TV launcher entries. TV support and touch input
are optional manifest features so the same APK can install on all three targets.
Use TV Material components for future remote-control focus and navigation.
Phone, tablet, and TV Compose previews are included.
