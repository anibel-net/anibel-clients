# Windows releases

The Windows app uses Velopack 1.2.0 and unsigned GitHub release assets. Windows can show an unsigned-app warning.

## Version and channels

`apps/windows/version.props` is the default local version. A release tag overrides it for the executable, installer and update package:

- `windows-v0.2.0`: stable, channel `win-x64`.
- `windows-v0.3.0-beta.1`: preview, channel `win-x64-preview`.

Use a new version for each release. Do not replace a published package with a different build of the same version. To recover from a bad release, publish the fix with a higher version. Downgrades are disabled.

Stable installations only read the stable channel. Testers install the preview installer to use preview releases. Uninstalling/reinstalling the other channel does not remove the separate user data directory.

## Configure GitHub

The release repository must be public. The app contains no GitHub access token.

By default, the workflow hosts releases in its own repository. If source code is private, create a separate public release repository with an initial README commit and set these in the source repository's Actions settings:

- Variable `WINDOWS_RELEASE_REPOSITORY`: `https://github.com/OWNER/RELEASES`.
- Secret `WINDOWS_RELEASE_TOKEN`: a fine-grained token with Contents read/write access to that release repository. This token is used only in CI.

For releases in the same public repository, the workflow uses `GITHUB_TOKEN`.

## Publish

1. Commit the intended Windows changes and update the default version file.
2. Push a tag on that commit, for example:

   ```powershell
   git tag windows-v0.2.0
   git push origin windows-v0.2.0
   ```

3. The Windows workflow runs unit tests and native playback checks, builds Rust and the app, downloads the previous package for delta generation, and creates a **draft** release.
4. Add release notes and publish the draft on GitHub. The app cannot see drafts.

Keep the generated `releases.CHANNEL.json`, full `.nupkg`, delta `.nupkg` (when present), and Setup.exe together in each GitHub release. Velopack uploads these files. The ordinary ZIP and SHA-256 file are also attached. Do not delete old releases needed by users who skipped versions.

## Local packaging

Build the Rust core and native dependencies as described in the main README, then:

```powershell
./scripts/publish-windows.ps1 -Version 0.2.0 -Installer -UpdateRepository https://github.com/OWNER/RELEASES
```

The installer and update packages are in `artifacts/releases`. The ordinary ZIP remains in `artifacts/share`. To generate a delta locally, first put the previous full package in the release directory with `dotnet tool run vpk download github`.

The installer includes the app, Rust core, FFmpeg, libass and Windows/.NET runtime files. They update as one release. No separate DLL downloads are needed.

## User behaviour

- Ordinary ZIP/development builds display installation instructions instead of checking for updates. Existing ZIP users install Setup.exe once.
- Installed builds check 15 seconds after launch and every six hours. Settings also has a manual check button.
- A discovered update downloads in the background. Velopack verifies the package size/hash and can fall back from a delta to a full package.
- The app displays a notification when the update is ready. The user can leave it for later.
- Restart is explicit and is blocked while a player/reader is open or a media download is active. The app closes playback and the Rust core before applying the update.
- Pending updates remain pending across app launches. Startup does not force an update.
- Settings and managed user data stay in `%LOCALAPPDATA%/Anibel`, outside the installation directory. Do not change the pack ID `Anibel.Net` or the user data path between releases.

## Validation

Run Windows tests and the native playback smoke suite before tagging. For release validation, install an older package in a test Windows user account, publish a newer draft to a separate test feed, then test check/download/restart. Draft GitHub releases are intentionally invisible to the production update client: use a local/static feed for unpublished update testing or publish only in a separate public test repository.

Check both full and delta updates, offline failure/retry, a pending update after relaunch, and that settings/downloaded files survive. The app's update service tests cover concurrent checks, retries, cancellation, pending state and explicit apply.

The `tools/UpdateSmoke` check downloads full and delta packages from a local release feed with the real Velopack SDK. It checks package hashes and the version staged for restart without replacing the running app. CI runs it when a previous package is available.
