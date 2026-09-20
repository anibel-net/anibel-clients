# Windows ownership

| Owner | Responsibility |
| --- | --- |
| `App` | Creates the service host. Starts and stops core polling. Stops image loading and the core before it disposes the host. |
| `CoreStatePump` | Runs one session and download refresh at a time. Stops its timer and waits for the current refresh on shutdown. |
| `MainPage` | Routes navigation and page messages. |
| `OpenMediaWorkspace` | Owns open media hosts, detached windows, full screen, and pending player close tasks. |
| `PlayerHost` | Connects controls to playback and displays state. |
| `PlayerSettingsMenu` | Builds track and quality choices. Ignores actions for a replaced engine. |
| `PlayerController` | Owns the engine and core playback session. Keeps lifecycle reports in order and replaces waiting position reports with the latest position. |
| `WindowsMediaEngine` | Owns Windows playback, source changes, subtitles, and native cleanup. Keeps the requested seek position separate from playback position. |
| `MediaDetailsViewModel` | Owns title data and resource selection. Delegates comments to `MediaCommentsViewModel`. |
| `ImageLoader` | Owns the bounded UI image cache. The XAML converter only calls this service. |
| `ImageDiskCache` | Owns URL-based disk storage, concurrent downloads, and download cancellation. |
| `CoreClient` | Tracks native requests. Rejects new requests during shutdown and waits for active requests before it frees the core. |

UI owners and observable collections run on the UI thread. Do not move their updates to a worker thread. `CoreClient` protects its request and shutdown state with a lock.

Window close first closes media hosts and waits for playback reports and native cleanup. It then stops polling and image downloads, drains core requests, and disposes the service host.

## Checks

Run `dotnet test apps/windows/tests/Anibel.App.Tests.csproj -p:Platform=x64` from the repository root.

The Windows workflow also builds local media fixtures and runs `scripts/test-windows-playback.ps1` against a published app. Playback failures fail the job. The test covers native MP4, HLS, DASH, separate audio, MKV, subtitles, seeking, quality changes, and detached playback windows.

Windows playback remains the default. On a decoder/unsupported-source error,
WindowsMediaEngine opens FFmpegInteropX software decoding with the bundled FFmpeg 7
DLLs. The engine owns the bridge and temporary single-quality manifests until
playback closes. libass continues to own text subtitle rendering. The build targets
the Windows 11 SDK for the bridge projection; TargetPlatformMinVersion remains
Windows 10 (17763). Windows 10 device validation is still required.
