# Anibel Clients — Architecture

Version: 1.0 · Status: historical plan and backend notes

> Historical backend research and original design notes. The current implemented
> ownership and protocol are defined in [SHARED-CORE-SPEC.md](SHARED-CORE-SPEC.md).
> Rust now owns shared cache, session, downloads, playback/resume/history, reader,
> page cursors, content choices and personal-list rules. Windows supplies UI and OS adapters.
> Any future-tense milestones or old ownership descriptions below are historical.


Native clients for [anibel.net](https://anibel.net) — Belarusian anime/manga/cinema/games platform.

**V1 scope (verified against backend):**

- Catalog: home (slider/trends/wall of updates), catalog browsing + filters + search
- Media details: episodes (sub/dub, resources 1/2), chapters, marks/favorites, history
- Watch: login, marks (watching/watched/read/…), watched-episode tracking
- Player: video + separate audio track + ASS subtitles (+ embedded fonts) via **libass**, HLS/DASH or direct MP4, WebView2 fallback for external embeds

---

## 1. Findings — anibel.net backend surface

Verified from `anibel-monorepo` (new stack), `anibel-next` (old stack) and live introspection of `https://anibel.net/graphql`.

### 1.1 GraphQL API

- Endpoint: `POST https://anibel.net/graphql` (`{ query, variables }` JSON), CORS open
- Auth: `Authorization: Bearer <jwt>` — JWT returned by `login/ createUser/ verifyOauth`
- Errors: `errors: [{ message }]`, success: `{ data }`
- Pagination pattern: `{ docs, totalDocs, limit, offset }`

Queries used by clients (mirror of `@anibel/sdk`):

| Group | Queries |
|---|---|
| Media | `media(slug, mediaType)`, `getMediaList(offset, limit, mediaType, filters)`, `search(query, limit)`, `randomMedia` |
| Home | `getSlider`, `getTrends(type, date, limit)`, `getUpdatesList(type, offset, limit)`, `getRecommendations(type, limit)`, `getSchedule`, `getStatistics`, `getStatisticsHistory` |
| Episodes | `episodes(mediaId, type: sub|dub, resource: 1|2, offset, limit)` (sorted by `episode` desc) |
| Manga | `chapters(mediaId)`, `chapter(slug, chapter)`, `volumes(mediaId)`, `volume(mediaId, volume)` |
| User | `user(username)`, `favorites(username, mediaType)`, `marks(username, mediaType)`, `status(username, mediaType)` — public profile views; `me` exists only in the checked-in new schema (see drift note §11) |
| Misc | `comments`, `getFilters(mediaType)`; admin/staff only: `users`, `latestComments`, `reports`, `uploads` |

**Auth tiers — verified against production (Aug 2026, no token):** everything for the client scope is public except the `users/latestComments/reports/uploads` staff surface and all state-mutating mutations. Read-only tier works with zero token:

| Public, no token | Requires login |
|---|---|
| `search`, `media`, `getMediaList`, `episodes`, `chapters`, `chapter`, `volumes`, `volume` · `getTrends`, `getUpdatesList`, `getSchedule`, `getSlider`, `getFilters`, `getRecommendations`, `getStatistics`, `getStatisticsHistory`, `randomMedia` · `user`, `favorites`, `marks`, `status`, `comments` | Queries: `users`, `latestComments`, `reports`, `uploads` (staff/admin — not used by client) · Mutations: `addFavorite/removeFavorite`, `markAs/removeMark`, `addComment`, `updateUser`, `upload` |
| Probes: `favorites`/`marks`/`chapter` 500 on *nonexistent* user/slug — resolver quirk, not an auth issue | `addHistoryRecord/removeHistoryRecord/addRating/mediaReport` are *not* shielded (fallback-allow) but resolve against the context user; assume auth-required in code until verified with a token |

> Gate mechanics: production runs the old backend wrapped in graphql-shield with fallback-allow but `fallbackError` = *"you must be logged in…"* — **one gated field in a multi-field query poisons the whole batch**. The Rust client must always issue single-purpose queries per call op (never aggregate unrelated fields into one request).

Filters (`getMediaList`): `language[sub|dub]`, `genres[]`, `studies[]`, `year[]`, `status[finished|ongoing]`, `country`, `type[]` (content type), `translators[]`, `dubbers[]`, `editors[]`, `hidden`, `query` (text search).

Media types: `anime|manga|cinema|games|books` · Text is multilingual `{ ru?, be!, en?, alt[] }`.

Mutations for clients: `login`, `createUser`, `updateUser`, `markAs`, `removeMark`, `addFavorite`, `removeFavorite`, `addHistoryRecord`, `removeHistoryRecord`, `addRating`, `addComment`, `mediaReport`.

Mark statuses: `notselected|watching|watched|dropped|planned|playing|read|reading|played`.

### 1.2 Episode → player → media sources (verified against production)

An episode carries:

```
Episode { id, episode, endEpisode?, title?, type: sub|dub, resource: 1|2, url, released?, watched? }
```

- `resource: 1` — Google Drive embed URL (`drive.google.com/…/preview`). No direct media extraction; WebView2 pipeline.
- `resource: 2` — Anibel Player page: `https://video.anibel.net/<uuid>?type=anime`. The uuid is the **video service record id**.

**Playback resolution (implemented in core, verified live):**

| Step | Endpoint | Result |
|---|---|---|
| resolve record | `POST https://api.anibel.stream/video` (multipart `videoId`, **public**) | `title`, `meta{width,height,codec,duraction,…}`, `hls` (`/dash/<id>/manifest.m3u8`), `stream` (MPD), `host` (CDN node, e.g. `https://n3.anibel.stream`), `subtitles[]{path: <ass url>, fonts[]}`, `support{dub,sub}`, `episode/season/groupBy` |
| font assets | `POST {videoApi}/fonts-by-names` `{fontNames:[…]}` (public) | direct `.ttf` URLs (`https://fonts.anibel.net/…`) for libass |

The player page itself is an SPA (no HTML parsing possible/needed). Stream URL = `host + hls` (HLS master, Windows native). Dub tracks are *separate video records* (different episode `url`), not a separate audio file. Prior `video(videoId)` GraphQL query is dead on production.

### 1.3 Auth

- `login(username, password) → User { id, username, role, token, avatar }` — JWT; token then used as `Authorization: Bearer`
- **`me` does not exist on production** (it's in the new checked-in schema only) — session restore uses stored profile from the login response (+ `user(username)` if a fresh fetch is needed). Tracked in §11.
- OAuth (`tg | vk | google`) + email verification exist; V1: username/password login, OAuth later via WebView2
- Video service `api.anibel.stream` is admin/upload only → **out of scope** for clients
- Introspection (`__type`) works unauthenticated — schema pinning via `scripts/fetch-schema.ps1` needs no token

---

## 2. Architecture goals

1. **One shared core** for all platforms: Rust. Contains GraphQL client, typed models, auth/session, playback intent & source resolution. Zero UI.
2. **Thin per-platform layer**: native UI + native rendering (player surface, storage, OAuth, package) only.
3. **Small scope, big seams**: catalog/watch/player first; everything else can be added without reshaping.
4. All platform-specific code lives under `apps/<platform>/`, protected by traits/interfaces in the core.
5. No secrets in repo; tokens in OS-protected storage.

---

## 3. Repository layout

```
anibel-clients/
├── README.md
├── docs/
│   └── ARCHITECTURE.md          # this file
├── Cargo.toml                   # workspace
├── rust-toolchain.toml          # pinned Rust 1.95.0 (minimal: rustfmt + clippy)
├── crates/
│   ├── anibel-domain/           # frozen DTOs + AnibelError
│   ├── anibel-api/              # GraphQL: schema.graphql + ops/*.graphql, modules split by domain
│   │   └── src/
│   │       ├── transport/       # HTTP transport + schema-generated client
│   │       ├── auth/            # login/logout/session ops
│   │       ├── catalog/         # mediaList/search/filters/episodes/chapters
│   │       ├── media/           # media details/comments/marks/favorites
│   │       ├── home/            # slider/trends/updates/schedule/statistics
│   │       ├── user/            # public profile ops
│   │       └── mutations/       # markAs/favorites/history/rating ops
│   └── anibel-player/           # video service + PlaybackIntent (lib/video/player)
├── core/                        # C ABI cdylib facade (anibel_core.dll)
│   ├── Cargo.toml
│   └── src/
│       ├── lib.rs               # re-exports domain/api/player + ffi
│       └── ffi.rs               # extern "C" ABI + event queue
├── apps/
│   └── windows/                 # WinUI 3 / C# app (see §5) — no .sln, target the csproj
│       ├── src/
│       │   ├── Anibel.App.csproj    # versions pinned inline (no CPM)
│       │   ├── App.xaml(.cs)        # DI + lifecycle
│       │   ├── Core/                # AnibelCoreNative (P/Invoke), CoreClient
│       │   ├── Playback/            # IPlayerEngine, MpvEngine, WebView2Engine, PlayerHost
│       │   ├── Reading/             # ReaderHost (manga)
│       │   ├── Services/            # Navigation, Settings, SecureStore, downloads
│       │   ├── ViewModels/
│       │   ├── Views/               # Home, Catalog, Details, Search, Profile, Downloads, Settings
│       │   └── Assets/
│       └── tests/                  # xUnit (ViewModelTests, PlaybackControllerTests, DownloadTests)
├── tools/
│   └── UiSmoke/                 # UI Automation smoke driver (see §9)
├── scripts/
│   ├── bootstrap.ps1               # install rust targets, winui build deps
│   ├── build-core.ps1              # cargo build --release && copy DLL to app
│   ├── build-app.ps1               # dotnet build/package MSIX
│   ├── fetch-mpv.ps1               # patched libmpv binary
│   ├── fetch-schema.ps1            # refresh graphql schema.graphql
│   └── (graphql op helpers: split_graphql_ops.py, expand_gql_ops.py)
└── .github/workflows/
    ├── core.yml                    # fmt/clippy/test (windows + linux, pinned 1.95.0)
    └── windows.yml                 # build app + UI smoke + bundle core → signed MSIX
```

Rule of thumb: **modules inside `core/`** until two conditions hold — code is genuinely reusable by more than one consumer *and* compile-time isolation (separate feature gates) pays off. Only then split into `core/runtime-api`, `core/playback`, etc. Apps never reference Rust modules directly; they talk to the FFI surface only.

Future platforms: `apps/android/` (Kotlin), `apps/ios/` (Swift), `apps/macos/` (Swift). Same core crate, different FFI adapter (JNI / Swift interop / C ABI).

---

## 4. Shared core — `core/` (Rust)

### 4.1 Responsibilities

- HTTP transport for GraphQL (reqwest + native-tls (SChannel — Cloudflare-required), browser-ish UA/headers, cookie jar)
- **Schema-driven GraphQL client** (`graphql_client` derive from `crates/anibel-api/graphql/schema.graphql` + one file per op in `graphql/ops/`) — the gateway rejects unused ops/fragments, so each request ships a single document. `scripts/fetch-schema.ps1` refreshes the SDL.
- **Video Service client** (`crates/anibel-player/src/video.rs`): `POST /video?videoId` (form), `POST /fonts-by-names` — playback source contract
- Frozen domain DTOs on the FFI wire (MediaCard, MediaDetail, Episode, Comment, Pagination, PlaybackIntent, User). GraphQL generated types stay crate-private; the api crate maps gql → domain (BigNumber → i64, `date.created` → `created`, `studies` → `studios`)
- Auth session: token storage is **host-side** (OS secure store); core only holds it in memory
- Playback resolution: `Episode → PlaybackIntent { kind, videoSrc, subtitles[], fonts[], durationSecs }`
- Media image URLs are passed through untouched; **no image handling in core V1**
- Errors in machine-readable form; retries/rate-limit awareness

### 4.2 FFI contract (C ABI, `cdylib`)

Single translation boundary: **JSON in, JSON out**, one blocking call + pollable event queue. Simple to bind from any host (C#, Kotlin, Swift) and easy to debug.

```c
// function-symbol export, MSVC-linkable
int64_t anibel_core_init(const char* config_json);        // -> handle, -1 on error
char*   anibel_core_call(int64_t handle, const char* req); // blocking; caller frees string
char*   anibel_core_events(int64_t handle);               // drain pending events (JSON array), "" if none
void    anibel_core_free(char* ptr);
void    anibel_core_shutdown(int64_t handle);
```

Request:

```json
{ "id": 1, "op": "mediaList", "args": { "mediaType": "anime", "offset": 0, "limit": 20, "filters": { "language": ["sub"] } } }
```

Response: `{ "id": 1, "ok": true, "value": { … } }` or `{ "id": 1, "ok": false, "error": { "code": "http_401", "message": "…", "cause": "…" } }`.

Async uses tokio internally; `anibel_core_call` blocks on `oneshot` (host calls from thread-pool, never from the UI thread).

Events currently emitted (drained via `anibel_core_events`, ~30–60 ms poll from `DispatcherQueueTimer`). Playback is request/response (`resolveEpisode`); do not invent a player event queue:

```json
{ "e": "auth.expired" }
{ "e": "error", "detail": { "code": "…", "message": "…" } }
```

### 4.3 Ops (V1)

`login, logout, me, media, mediaList, search, slider, trends, updates, recommendations, schedule, filters, statistics, episodes, chapters, chapter, marks, favorites, status, markAs, removeMark, addFavorite, removeFavorite, addHistoryRecord, removeHistoryRecord, resolveEpisode(episodeId|url)`

### 4.4 Cross-platform policy

- No Windows-only deps in core; platform things (secure storage, keychain, DPAPI) stay in host apps
- Playback engines remain host-side. Desktop MKV download jobs invoke host FFmpeg/ffprobe tools; the core owns their cancellation and output files.
- WASM-gen possibility later (browser fallback) as a bonus, not a target

---

## 5. Windows app — WinUI 3 (C#)

### 5.1 Stack & dependencies

| Concern | Choice (verified Aug 2026) |
|---|---|
| Runtime | .NET 10 SDK / TFM `net10.0-windows10.0.19041.0` (LTS line), C# latest |
| UI | WinUI 3 · **Windows App SDK 2.4.0 (stable 2.x, Aug 2026)** — Win10 1809+ supported; 1.8.x still serviced but start on 2.x |
| Templates | VS 2026 (WinUI/WinAppSDK workload) **or** CLI: `dotnet new winui` (package identity handled by `Microsoft.Windows.SDK.BuildTools.WinApp`) |
| MVVM | **CommunityToolkit.Mvvm 8.4.2** (source-gen `[ObservableProperty]` partial props, `[RelayCommand]`) |
| DI/lifecycle | `Microsoft.Extensions.Hosting` 10.x in `App.xaml.cs`, VMs resolved via DI |
| Navigation | `NavigationView` shell + `Frame`; route model for deep links (`anibel://media/{slug}`, `anibel://episode/{id}`) |
| Storage | JSON settings + token via `ProtectedData` (DPAPI) in `LocalApplicationData` |
| Player surface | `MediaPlayerElement` + libass `Image` overlay + `WebView2` |
| WebView2 | **Microsoft.Web.WebView2 1.0.4191.x** (evergreen runtime, WinUI 3 supported package) |
| Tests | xUnit + mocking of `ICoreClient` |

All versions are pinned inline in `Anibel.App.csproj` — there is no `Directory.Packages.props` (Central Package Management) and no `.sln`; build/package the csproj directly.

### 5.2 App layer

```
App (DI bootstrap)
 ├─ CoreClient            (P/Invoke → anibel_core.dll, Task<T> wrappers, event pump)
 ├─ SessionService        (token in/out of DPAPI store → CoreClient.login; profile from login response — no `me` on prod)
 ├─ NavigationService     (route mapping, back stack)
 ├─ PlayerController      (state machine, calls CoreClient.resolveEpisode → engine)
 ├─ engines: MpvEngine | WebView2Engine (IPlayerEngine)
 └─ SettingsService
```

Core contract in C#: `AnibelCoreNative` (DllImport) + `CoreClient` mapping ops → typed `Task<T>`; mapping errors → `AnibelException { Code, Message }`.

`anibel_core.dll` is produced by `scripts/build-core.ps1` and dropped next to the exe; app startup version-checks it (`core.version` op) and shows an error dialog on mismatch.

### 5.3 Views (V1)

- `ShellPage` — NavigationView: **Галоўная** (slider + trends + updates), **Каталог** (filters/search/grid), **Закладкі/Глядзелкі**, **Пошук**, **Налады**; account flyover (login/logout)
- `HomePage` — slider banners, trending rows, updates wall (paged)
- `CatalogPage` — filter drawer + patch infinite list (offset/limit), media cards (poster/title/type/rating)
- `MediaDetailsPage` — description, genres, studios, marks (status/favorite/rating), episode list (sub/dub tabs, resource switcher, watched toggles), chapters for manga
- Playback is hosted in the `Playback/PlayerHost` overlay (video surface + controls + episode sidebar, `PipWindow` mini-player) — the old `PlayerPage` was removed
- `LoginPage` — form + error handling (later OAuth via WebView2)
- `SettingsPage` — API URL override (dev), cache clear, language (ru/be/en)

> Note: app-wide events (session changed/expired, open media, play episode, navigate, …) are delivered through CommunityToolkit `WeakReferenceMessenger`; the static `AppEvents` hub was removed.

---

## 6. Playback pipeline

### 6.1 Engines

| Engine | Use case | Pros | Cons |
|---|---|---|---|
| **Windows MediaPlayer + libass — default** | resource 2 and offline MKV | Windows H.264/AAC codecs, native HLS/DASH, shared timeline for separate audio, libass styles/fonts | other codecs depend on installed Windows support |
| WebView2 | resource 1 (Google Drive embeds) | uses the service embed | embed controls remain service-owned |

`WindowsMediaEngine` attaches a Windows MediaPlayer to a WinUI MediaPlayerElement.
A MediaTimelineController synchronizes video and optional separate audio. The
existing player controls set the timeline position/state and media track indices.
AdaptiveMediaSource exposes bitrate choices; Auto clears the requested bounds.

`AssRenderer` uses libass with DirectWrite font discovery and downloaded fonts.
The overlay blends libass masks into a premultiplied BGRA WriteableBitmap only
when the subtitle frame changes. It follows the fitted video rectangle through
resizes and PiP transfers. Rendering is bounded to 3840 by 2160 pixels.
`NativeSubtitleAssets` extracts offline MKV text subtitles and font attachments
with the small FFmpeg tools; temporary files are removed after playback closes.

### 6.2 Playback data flow (resource 2)

```
C# PlayerController
  ├─ CoreClient.resolveEpisode(videoId | episodeUrl)
  │    ├─ core extracts uuid from url (or takes videoId)
  │    ├─ POST api.anibel.stream/video  → VideoInfo (host, hls, .ass tracks, fonts[])
  │    ├─ POST /fonts-by-names          → font .ttf urls (family → asset)
  │    └─ PlaybackIntent {
  │         videoSrc:  host + hls m3u8 (DASH fallback),
  │         subtitles: [{ url: <ass>, fonts: [families] }],
  │         fonts:     [{ family, url }],
  │         durationSecs, videoId, kind: native | embed
  │       }
  ├─ IPlayerEngine.Load(intent)  // WindowsMediaEngine | WebView2Engine
  │    ├─ download fonts[] → local font dir (cache)
  │    ├─ MediaPlayer: videoSrc (HLS/DASH/file), shared audio timeline
  │    ├─ libass: font dir + ASS styles
  │    └─ engine.ReadPosition/SetPosition/Rate/Quality
  └─ on position updates: throttled save; auto addHistoryRecord on play/end
```

### 6.3 Subtitle policy

- `subtitles[].path` ASS → libass overlay (tracks selectable)
- Remote fonts (`fonts-by-names` → `fonts.anibel.net/*.ttf`) → downloaded once into `localsettings/fonts/<hash>.ttf` and loaded by libass through its fonts directory
- Subtitle on/off and track selection stay host-side; ASS styles and positions come from the subtitle file.

---

## 7. Key flows

**Boot:** app starts without any login — public content is directly usable. If a stored token exists: `CoreClient.login`-less restore (`SessionService` sets token + cached profile; optionally refresh via `user(username)` when the profile view opens). Offline: cached last-route.

**Catalog fetch:** `mediaList { offset, limit, filters }` (filters from `getFilters`); infinite scroll; request cancellation on filter change.

**Play episode:** fetch `episodes(mediaId, type, resource)` for current resource → resolve selected episode → `resolveEpisode` → engine load → if logged in, submit `addHistoryRecord(type: episode, entityId)` on start (anonymous users just watch).

**Resume:** remember `{ mediaId, episode, positionMs }` locally; `player.intent` may include `resumeMs`; media details page shows a «Continue» chip.

---

## 8. Builds, CI & packaging

- Toolchain: Rust pinned to **1.95.0** in `rust-toolchain.toml` (minimal profile, `rustfmt` + `clippy`); CI uses the same version
- `core.yml`: PR → `cargo fmt --check`, `clippy -D warnings`, `test` on **windows-latest + ubuntu-latest** (parallel jobs; the best-effort live API smoke stays windows-only); release tag → artifact `anibel_core.dll` (msvc x64)
- `windows.yml`: `dotnet restore/build/test`, then best-effort Debug build + UI smoke (`tools/UiSmoke`, see §9), then pack MSIX with core DLL included; unsigned for CI, signing via `signtool` in release pipeline
- Dev bootstrap: `scripts/bootstrap.ps1` (winget: .NET 10 SDK, VS 2026 + Windows App SDK/WinUI workload; `rustup target add x86_64-pc-windows-msvc`); mpv binary via `scripts/fetch-mpv.ps1`
- Run: `scripts/build-core.ps1 && scripts/build-app.ps1` then launch unpackaged (`-p:WindowsPackageType=None`) for fast local iteration
- mpv binary (GPL) pinned version + hash; release distribution must include GPL compliance info and vendored source link

---

## 9. Testing

- **Core (Rust):** unit tests on domain mapping (BigNumber, comment dates, FFI arg shapes) and API with `httpmock` for the video service; optional live GraphQL smoke (`--ignored`)
- **Core (integration):** live-API smoke (optional, `--ignored`, flagged `[live]`) — run rarely to detect schema drift
- **App (C#):** unit tests for VMs and the player controller with a mocked `ICoreClient` — `tests/ViewModelTests.cs`, `tests/PlaybackControllerTests.cs`
- **UI smoke:** `tools/UiSmoke` drives the real WinUI3 app via Windows UI Automation (no WinAppDriver) — app path via `ANIBEL_APP_EXE` env var or CLI arg; run best-effort in `windows.yml` CI
- Schema drift guard: `scripts/fetch-schema.ps1` downloads `schema.graphql` from `https://anibel.net/graphql` into `crates/anibel-api/graphql/`; CI fails if core schema-dependent tests mismatch

---

## 10. Milestones (V1 — Windows)

| M | Content | Exit criteria |
|---|---|---|
| M0 | Repo setup: workspace, CI (core+win), core stub FFI with `health`/`version`, WinUI shell + NavigationView + DI + `CoreClient` ping | ✅ app boots, calls core, version check green |
| M1 | **Read-only, zero token**: catalog (`mediaList`, `search`, `getFilters`), home (slider/trends/updates/schedule), media details + episodes/chapters + comments, public profiles (`user`/`marks`/`favorites`/`status` by username) — no player yet | ✅ browse/filter/search/comments/profiles work logged-out |
| M2 | Auth + personal layer: login/logout (DPAPI + core session restore), `markAs`/`addFavorite`/`addHistoryRecord` + watched toggles in details, history submit from player | ✅ login persists; marks visible; anonymous flow still works |
| M3 | Player: `resolveEpisode` (video service) + `MpvEngine` (libmpv + libass + fonts), episode sidebar (sub/dub, resources, prev/next), history submit (when logged in), resume | ✅ engine code complete (verified pattern); visual accept pending |
| M4 | WebView2 engine for resource 1 (Google Drive embeds) + settings page, cache clear, language (ru/be/en) | ✅ implemented; MSIX signing/publish stays a release-time task |

Remaining polish (tracked): visual playback verification (subtitles/fonts render), resume position persistence, `mediaReport` submission from the player, MSIX packaging/signing for Store distribution.

Out of scope V1: manga reader (images), ratings uploads+profile pages, Telegram/VK OAuth, downloads, comments moderation, streaming quality picker (mpv auto-picks), Linux/macOS/mobile builds (core FFI is ready though). The watched-EP/"continue" history and media report are the next fast wins.

---

## 11. Open questions / risks

1. ~~open question A (public episode→sources)~~ **Resolved:** public video-service endpoints `POST /video` + `POST /fonts-by-names` (verified) are the resolution contract. Remaining risk: **contract churn** — it's business logic, not a documented API; pin live tests (`core/tests/live.rs`) to catch drift.
2. **Production ≠ repo schema (drift, verified):** prod runs the old backend — `me` missing (use login response), `favorites/marks/chapter` throw 500s on missing user/slug (resolver quirk, catch in core and surface as `not_found`), `uploads` broken on prod, `getSuite` etc. Core must be resilient: schema-failing → per-op error, never app crash.
3. **Shield batching trap:** one gated field poisons a multi-field query — core client must issue one op per request (single-root queries only).
4. **mpv binary supply chain:** patched libmpv (zhongfly/mpv-winbuild) is community-built — pin exact version, hash it, keep MPV-lazy lineage upstream changes tracked; GPL terms on distribution.
5. **Fonts/ASS edge cases:** remote fonts list + font-dir loading — verify with real ASS samples during M3 spike (compare vs site's SubtitlesOctopus output).
6. **HLS multi-audio:** confirm whether `audioSrc` is an independent file or the HLS alt-audio group — affects engine wiring.
7. **Packaging:** MSIX vs unpackaged for distribution (decide at M4; core DLL loading works in both).
