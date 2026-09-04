# Anibel Clients

Native clients for [anibel.net](https://anibel.net) (anime / manga / cinema / games platform).

Monorepo with:

- `core/` — C ABI FFI (`anibel_core.dll`) over workspace crates
- `crates/anibel-domain` — frozen DTOs + errors
- `crates/anibel-api` — GraphQL (one `.graphql` file per op) + mapping
- `crates/anibel-player` — video service + playback intent
- `apps/windows` — WinUI 3 (C# / .NET 10) app: UI + native layer (libmpv player engine, WebView2 fallback)
- future: `apps/android`, `apps/ios`, `apps/macos`

## Quick start

```powershell
scripts/bootstrap.ps1     # deps: .NET 10 SDK, rustup, WinUI templates
scripts/fetch-mpv.ps1     # libmpv-2.dll (patched mpv-winbuild) — once + on bump
scripts/build-core.ps1    # cargo build --release -p anibel-core (DLL auto-copied to app)
scripts/build-app.ps1 -Configuration Debug -Run
```

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

Current status: **feature-complete for v1 catalog + player** — read-only catalog/trends/slider/updates, media details with episodes/chapters/comments, login (DPAPI token, restored into core), marks/favorites/watched tracking, native playback via libmpv (`resolveEpisode` → video service HLS + .ass subtitles + fonts) with a WebView2 fallback for Google Drive embeds, settings page. Core contract & playback resolution verified live; final visual accept of subtitles/fonts pending on-device.

## Docs

- [Architecture](docs/ARCHITECTURE.md) — full plan, backend API notes, playback pipeline, milestones
