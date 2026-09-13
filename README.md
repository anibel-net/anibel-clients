# Anibel Clients

Native clients for [anibel.net](https://anibel.net) (anime / manga / cinema / games platform).

Monorepo with:

- `core/` — shared Rust application state and policy, exposed through the C ABI (`anibel_core.dll`)
- `crates/anibel-domain` — shared models + errors
- `crates/anibel-api` — GraphQL (one `.graphql` file per op) + mapping
- `crates/anibel-player` — video service + playback intent
- `apps/windows` — WinUI 3 (C# / .NET 10) app: UI + native layer (libmpv player engine, WebView2 fallback)
- future: `apps/android`, `apps/ios`, `apps/macos`

## Shared core and native clients

Rust is the source of truth for shared application behaviour. Native clients own
screens, navigation, rendering, secure credential storage and OS integration.
They send commands to Rust and display its results; they must not duplicate its
business rules.

The [shared-core specification](docs/SHARED-CORE-SPEC.md) defines the implemented
ownership, protocol 2 commands, storage, lifecycle, limits, and tests.

The current Windows code uses Rust for response caching, session expiry,
favorites/marks/history, downloads and offline export, playback source and track
choices, resume data, reader source/navigation, page cursors, personal lists,
profile totals, and saved searches. The duplicate native policy services are removed.

Profiles show the avatar, wallpaper, display name, handle, and bio. Use **Edit
profile** on your own profile to change the name or bio, or select and remove
images. PNG, JPEG, GIF, and WebP files up to 10 MiB each are supported. Rust
validates the changes, uploads images, and reloads the saved profile.

Click a comment author's name or avatar to open their public profile. Replies
appear below their parent comment. The reply composer shows the selected author
and text, prevents duplicate sends, and keeps the draft if sending fails.

Signed-in users can rate a title with five stars in half-star steps. Click either
half of a star, or use arrow keys and Enter. Cards and title pages use the same
five-star display. The core and API keep the original 1–10 scale: 3.5 stars is 7.

Windows keeps WinUI view state, mpv/WebView2, file pickers, protected credentials,
localization, and OS integration. Swift/Kotlin apps and bindings are future work;
they must use the same core commands.

Each open title has its own reader or player. Multiple titles can use separate
PiP windows at the same time. The sidebar's open-title menu can show, float, or
close a title. Manga PiP has no native title bar. Move the pointer to show the
window and reading controls; drag the top title area to move the window.
The reading settings offer continuous scroll or one-page view, fit-to-page or
fit-to-width, zoom, and right-to-left page navigation. Use the page selector,
arrow keys, or Home/End to move between pages. In one-page view, Page Up/Down
also changes pages; the mouse wheel does so when the whole page fits at 100%.
Each open reader keeps its own reading mode and page when moved into or out of PiP.

Use the video settings button in the main player or PiP to select an available
quality. The menu lists actual video tracks, such as 1080p, 720p, or 360p. A
quality change keeps the current position, pause state, audio, and subtitles.
Sources with one quality are identified in the menu. Embedded players keep their
own quality controls.

This is a new-project data format. Old Windows cache, library, resume and
credential files are not imported. The core and app must be rebuilt together.

## Quick start

```powershell
scripts/bootstrap.ps1     # deps: .NET 10 SDK, rustup, WinUI templates
scripts/fetch-mpv.ps1     # libmpv-2.dll (patched mpv-winbuild) — once + on bump
scripts/build-core.ps1    # cargo build --release -p anibel-core (DLL auto-copied to app)
scripts/build-app.ps1 -Configuration Debug -Run
```

## Keyboard

Press **F1** or **?** for shortcut help. The sidebar also has a shortcut-help button.
Use **/** or **Ctrl+K** to focus search. Press **g**, then a page key within 1.5
seconds: **h** home, **e** search, **a** anime, **m** manga, **c** cinema, **g** games,
**b** books, **p** profile, **d** downloads, or **s** settings. **Alt+Left** goes back.
These app shortcuts do not run while a text field is active.

Cards use native keyboard focus: **Tab/Shift+Tab**, **arrow keys**, **Home/End**,
and **Enter/Space** to open. **j/k** moves to the next/previous card. The video in
focus supports **Space/k** to pause, **m** to mute, **f** for full screen, and
**Left/Right** to seek. Each PiP window controls its own media.

## Verify

```powershell
cargo test --workspace                                 # unit + model + codegen tests
cargo test -p anibel-core --test live -- --ignored     # live smoke vs production (schema drift guard)
dotnet test apps/windows/tests/Anibel.App.Tests.csproj -p:Platform=x64
dotnet build apps/windows/src/Anibel.App.csproj -c Debug -p:Platform=x64
dotnet build tools/UiSmoke/UiSmoke.csproj -c Debug
$env:ANIBEL_APP_EXE = "apps/windows/src/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/Anibel.App.exe"
& .\tools\UiSmoke\bin\Debug\net10.0-windows10.0.19041.0\UiSmoke.exe    # UI smoke (also best-effort in CI)
scripts/fetch-schema.ps1                               # refresh SDL → crates/anibel-api/graphql/schema.graphql
```

CI: `core.yml` runs `fmt`/`clippy`/`test` on `windows-latest` and `ubuntu-latest` (Rust pinned to 1.95.0 via `rust-toolchain.toml`), and `windows.yml` adds a best-effort UI smoke via `tools/UiSmoke`.

Current Windows feature coverage — read-only catalog/trends/slider/updates, media details with episodes/chapters/comments, login (DPAPI token, restored into core), marks/favorites/watched tracking, native playback via libmpv (`playbackOpen` → core source, resume and asset policy) with a WebView2 fallback for Google Drive embeds, settings page. Core contract & playback resolution verified live; final visual accept of subtitles/fonts pending on-device.

## Docs

- [Shared-core specification](docs/SHARED-CORE-SPEC.md) — current ownership, commands and verification
- [Architecture](docs/ARCHITECTURE.md) — historical backend notes, playback pipeline and original milestones
