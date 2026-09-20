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

## Feature structure

`Shell` owns windows, routing and toolbar search. `Features` groups related views,
view models and command adapters. `Core` owns the typed command boundary;
`Platform` owns OS and storage adapters; `UI` contains shared native presentation.
Namespaces remain stable while source folders express ownership.

ToolbarSearch owns suggestions, their cancellation and explicit focus requests.
MediaDownloadActions builds title download requests without owning XAML status controls.
SoftwareVideoSource owns the decoder and its temporary manifest. SubtitlePresentation
owns subtitle assets and rendering. The media engine remains the single timeline owner.
CoreStatePump uses snapshots as state and events as diagnostic notifications. It polls
active jobs more frequently than an idle library.

Windows prefers advertised DASH sources. Known unsupported H.264 profiles in
manifest metadata enter software decoding before a Windows decoder is attached.
SoftwareVideoSource captures stream metadata before playback starts; the UI must
not call decoder getters that take the read/seek lock. Software seeks use exact
timestamps and keep the loader active until native video and separate audio
acknowledge the seek. Repeated seek requests retain only the newest target.
Quality replacement keeps the shared clock and skips a redundant seek when its
new source is already at the requested position. Paused playback need not emit a
seek-completion event for an unchanged position.

Remote software-decoded sources read compressed packets ahead, bounded to 30 seconds
and 32 MiB per stream. This separates network segment reads from sample delivery.
Local files keep on-demand reads. These limits do not allocate decoded frame buffers.
