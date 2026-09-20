# Android app

Root AGENTS.md also applies. Read README.md and app/build.gradle.kts for supported SDKs and build setup.

## Implementation

- Use Kotlin, Jetpack Compose and platform Material components. Preserve both mobile and TV flows.
- Rust owns application policy. CoreClient is the JNI/command boundary; do not call GraphQL directly from screens.
- Keep blocking core calls and file work off the main thread. Tie coroutine work to the correct lifecycle and ignore replaced requests.
- Preserve Media3 and libass lifecycle, audio focus, system PiP and subtitle font loading. Do not create a second player on recomposition.
- TV must remain usable with D-pad and Back. Mobile layouts must support phones and tablets.
- Keep Belarusian and English string resources consistent. Do not add hard-coded visible strings to composables.
- Use Android Keystore for credentials and the system document picker for exported files. Do not add local SDK paths or signing keys to Git.
- Keep ABI builds for arm64-v8a and x86_64 and use the configured NDK version.

## Checks

From apps/android:

```powershell
./gradlew.bat :app:assembleDebug :app:lintDebug
./gradlew.bat :app:assembleDebugAndroidTest
```

Run relevant instrumentation tests on the appropriate device. Playback needs a rendered frame, not only a ready state. LivePlaybackTest and LiveReaderTest use production media and require explicit network testing. Check both phone and TV when changing shared navigation. Report emulator/API limits instead of claiming unrun tests passed.
