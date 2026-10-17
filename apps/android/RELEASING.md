# Android releases

Android releases use signed APK files in this public repository's GitHub Releases.
The APK includes the Rust core for ARM64 and x86_64. One APK supports phones,
tablets, Android TV, and Google TV on Android 8.0 or newer.

## Signing setup

Create one release keystore and keep an encrypted backup outside this repository.
Keep its passwords with the backup. Use the same key for future updates.
Do not generate a replacement key for each release.

Configure these repository Actions secrets:

| Name | Value |
| --- | --- |
| `ANDROID_KEYSTORE_BASE64` | Complete keystore file encoded as one Base64 line |
| `ANDROID_KEYSTORE_PASSWORD` | Keystore password |
| `ANDROID_KEY_ALIAS` | Private key alias |
| `ANDROID_KEY_PASSWORD` | Private key password |

If the four values are in a local dotenv file, upload them with:

```powershell
gh secret set --repo anibel-net/anibel-clients --env-file $SigningSecrets
```

Set `$SigningSecrets` to the private secrets file first. Never commit that file.
Base64 does not encrypt the key. CI restores the key in a temporary folder, signs
the checked APK, and removes the temporary folder. Gradle does not receive the key
or passwords. Pull request and branch checks do not receive signing secrets.

## Version and channels

`version.properties` is the single source for the app version:

- `versionName`: `X.Y.Z` or `X.Y.Z-beta.N`.
- `versionCode`: a positive integer, at most 2100000000.

Increase both the release version and `versionCode` for every release, including
the change from a beta to a stable version. CI checks against the highest
published Android release. It does not permit a lower version or reused code.
Version components and beta numbers must fit a signed 32-bit integer.

Stable installations read stable Android releases only. Beta installations read
both beta and stable Android releases, and can advance to the stable release of
the same version. To join beta testing from a stable version, install a newer beta
APK manually. Both channels use the same package ID, signing key, and app data.
Installing a lower version code is not supported.

Windows releases and draft releases are excluded from Android update checks.
The app uses the public release API without a GitHub token. It does not use the
repository-wide latest release link. The release feed is fixed to
`anibel-net/anibel-clients` in `AndroidReleaseSource.kt`.

## Create a release

1. Update `version.properties` and commit the intended changes.
2. Push a tag on that commit, for example:

   ```powershell
   git tag android-v0.1.1
   git push origin android-v0.1.1
   ```

3. CI builds the Rust core and both Android build types. It runs unit tests, lint,
   and the local emulator checks before passing the unsigned release APK to the
   separate signing job.
4. The signing job verifies the package ID, version, both Rust libraries, and the
   APK signature. It creates a **draft** GitHub release. Beta tags create drafts
   marked as prereleases. The workflow does not replace an existing release or
   change the repository's latest release selection.
5. Check the files, test installation, add release notes, and publish the draft.

Keep these files together in the release:

- `Anibel-Android.apk`: the signed app.
- `android-update.json`: version code, version name, APK size, and SHA-256 hash.
- `Anibel-Android.apk.sha256`: checksum for manual checks.
- `anibel-android-release-certificate.pem`: public signing certificate.
- `LICENSE.txt` and `THIRD-PARTY-NOTICES.txt`: license notices, also inside the APK.

Do not replace an APK or its metadata after publication. Publish a higher version
to correct a bad release. Keep the package ID `net.anibel.app` and the signing key.

## Local packaging

Use the SDK, NDK, JDK, and Rust setup from README.md. From the repository root:

```powershell
Push-Location apps/android
.\gradlew.bat :app:assembleRelease :app:lintRelease
Pop-Location
python scripts/package-android.py --apk apps/android/app/build/outputs/apk/release/app-release-unsigned.apk --output artifacts/android-release-0.1.1 --secrets-file $SigningSecrets
```

Python 3.11 or newer is required. Set `ANDROID_HOME` to the SDK directory and keep
the JDK tools on PATH. Use a new output directory for each build. Local packaging
checks and signs the APK; it does not upload files or create a release. Release
history is checked in CI when `--previous-releases` is supplied.

## User updates

Settings has a manual **Check for updates** button on mobile and TV. It offers a
download when a newer release is available. The full APK is downloaded into the
app's private files, with a 512 MiB limit. Partial downloads are removed on failure.
The app checks its size, checksum, package ID, version code, version name, and
signing certificate before offering installation.

Installation is explicit. If needed, Android first opens the setting to allow
installs from this app. The user then returns and selects **Install update**.
Android's installer asks for approval. Cancelled installation leaves the download
available. A valid pending APK is restored after the app process starts again.
Normal updates keep app data. The app does not uninstall itself.

Debug builds show release installation guidance and do not check for updates.
An existing debug installation normally needs to be uninstalled before the first
release installation because its certificate differs. Uninstalling removes local
app data. Later release updates install over the existing app.

## Validation

Run the standard Android checks and both release checks:

```powershell
python -m unittest discover -s scripts -p test_android_release.py
Push-Location apps/android
.\gradlew.bat :app:assembleDebug :app:lintDebug :app:testDebugUnitTest :app:assembleDebugAndroidTest :app:assembleRelease :app:lintRelease
Pop-Location
python scripts/create-android-update-fixtures.py
Push-Location apps/android
.\gradlew.bat :app:assembleDebugAndroidTest
.\gradlew.bat :app:connectedDebugAndroidTest -Pandroid.testInstrumentationRunnerArguments.class=net.anibel.app.AndroidUpdateTest
Pop-Location
```

`create-android-update-fixtures.py` uses the debug keystore created by
`assembleDebug`. It makes three small test APKs without the release key. These
fixtures are included in the test APK only.

`AndroidUpdateTest` uses local fixtures. It checks channel filtering, metadata
bounds, download sizes and checksums, and rejection of invalid, same-version,
wrong-package, or wrong-certificate APKs. No production account data
or live release is required. Before publication, test the system installer and
data retention with an older and newer APK signed by the release key on both
phone and TV. Draft releases are deliberately invisible to the update checker.
