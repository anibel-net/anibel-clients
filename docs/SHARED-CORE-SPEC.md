# Shared application core

Status: shared by Windows and Android. Protocol: 2. Updated: 2026-09-20.

## Purpose and scope

Rust is the source of truth for shared application behavior. Windows and Android
are native UI and OS adapters. Future Swift clients must use the same application
commands; Apple clients are not implemented here.

This is a new project. The app and core ship together. There is no import of old
Windows cache, download, resume, or credential files, and no compatibility layer
for the old native policy services. Old files are not deleted automatically.

## Ownership

| Area | Rust owns | Native host owns |
|---|---|---|
| Catalog | API access, cache, page cursors, episode choices, content and mark choices | Input, visible filters, grid layout, loading state, display formatting |
| Account | Active token, account revision, expiry, personal lists and totals | Login form and protected credential storage |
| Profiles and comments | Profile ownership, image validation and upload, profile updates, comment API and nested reply data | Image picker, edit draft, public profile navigation, reply target and tree layout |
| User changes | Favorite, mark and watched commands; cache invalidation | Requested selection and result display |
| Downloads | Queue, workers, retry/cancel/delete, asset manifests, local library and export | UI projection, folder picker, OS file access and execution time |
| Playback | Source choice, subtitle/font assets, default tracks, resume and history | Windows MediaPlayer/WebView2 or Android Media3, libass, surfaces, controls, audio focus and PiP |
| Reader | Online/offline page choice, previous/next chapter choice and history | Images, reading layout, page/scroll position, zoom and window controls |
| Storage | Data formats, bounds, atomic writes and asset cleanup | Private writable directory; DPAPI/Keychain/Android credential store |

Native view models may keep display copies and request generations. These prevent
old responses from replacing a newer screen. They are not durable application
state. Text translation, selected tabs, focus, and view visibility remain native.

## Structure

```text
Native UI -> typed binding -> Application::call
                                  | anibel-domain: models and errors
                                  | anibel-api: GraphQL and mapping
                                  | anibel-player: video service and source resolution
                                  | application: cache, session, downloads, playback, reader

C ABI: handles, request cancellation, JSON, panic containment, buffer ownership
```

The binding uses `Application`, not API adapters directly. Keep the application
modules in `crates/anibel-core/src/application`; do not add a crate or general repository
interface without a concrete dependency need. Download status, download kind,
playback kind, playback events, and personal-list kind are closed Rust enums.

The Windows services `DownloadService`, `SessionService`, and `SearchHistory` are
command adapters and display projections. `WindowsCredentialStore` contains the
Windows credential implementation. The former native cache, download workers,
HLS parser, library store, resume store, and track-policy classes are removed.

## ABI and lifecycle

```c
int64_t anibel_core_init(const char* config_json);
int32_t anibel_core_request_begin(int64_t handle, int64_t request_id);
void    anibel_core_cancel(int64_t handle, int64_t request_id);
char*   anibel_core_call(int64_t handle, const char* request_json);
char*   anibel_core_events(int64_t handle);
void    anibel_core_free(char* buffer);
void    anibel_core_shutdown(int64_t handle);
```

Init accepts `baseUrl`, `videoBaseUrl`, and `dataDir`. Use one live core per private
storage directory. Without `dataDir`, durable stores use a temporary directory
that is removed when their owners are dropped; the response cache stays in memory.
Invalid durable data causes init to fail with handle `-1`.

At startup, call `capabilities` and require `protocolVersion: 2`. Its feature list
includes downloads, playback, reader, session, cancellation and searchHistory.
The Windows app restores protected credentials before starting state polling.
Capabilities also include supported `downloadFormats`. Android starts downloads
suspended; its foreground service grants execution time, then drains workers on
stop. The desktop queue starts automatically. MKV downloads are not exposed on Android.

```json
{"id":17,"op":"media","args":{"slug":"example","mediaType":"anime"},"cache":"reload"}
```

Responses are `{id,ok:true,value}` or `{id,ok:false,error:{code,message}}`. Native
code maps error codes to UI text. Every returned C buffer must be freed once.
Call the blocking ABI on a worker thread. The core supports concurrent calls.

Reserve an ID with `request_begin` before scheduling the call. A successful
reservation returns 1. IDs must be unique among active requests; capacity is 1024.
Register host cancellation after reservation. Always call `anibel_core_call` for
a reserved ID, even if already cancelled, so the core can release the reservation.
A cancelled call returns error code `cancelled`. Cancellation drops active Rust
read/source work and its HTTP transfer. Session and backend mutations complete
once started, so the core observes their result and updates its cache and account
state. Cancellation can prevent these commands from starting, but it does not undo
a backend change or interrupt a mutation already in progress. Do not retry
mutations automatically.

A download is durable once enqueued. Cancelling its enqueue request does not act
as a job cancellation. Use `downloadChange` with `cancel` for that purpose.

Before host shutdown, stop issuing calls and wait for outstanding calls. Then
shut down the handle. Runtime shutdown stops workers. On the next init, persisted
`queued` and `downloading` jobs restart from their source when the first command
runs. Partial transfers are not byte-range resumed. A mobile host must provide OS
execution time, close the core when that time ends, and reopen it later. The core
does not claim to keep an app running after the OS suspends or terminates it.

## Application commands

Fields use camelCase. The source files contain the full DTO definitions.

| Command | Arguments | Result |
|---|---|---|
| `downloadsSuspend` | none | Drain workers and preserve queued jobs |
| `downloadsResume` | none | Grant execution time and start queued jobs |
| `continueEpisode` | mediaId, optional legacy selection | Last selected available episode, then first unwatched, then first episode |
| `rememberEpisode` | mediaId, episodeId, episodeType | Save the confirmed playback selection |
| `continueChapter` | mediaId, slug, optional legacyChapter | Saved available chapter or first chapter, plus chapter numbers |
| `session` | none | `{revision,authenticated,username,userId,avatar}`; no token |
| `login` | username, password | Login profile and token for protected host storage |
| `setToken` | token, username, id, avatar | Restores active session; null token clears it |
| `logout` | none | Clears active session |
| `profileHub` | none | Favorites and status totals; `unavailableTypes` identifies partial totals |
| `profile` | optional username; current account by default | Public profile and core-derived `isOwn` |
| `updateProfile` | optional displayName, bio, avatar, wallpaper, avatarPath, wallpaperPath | Saved profile; identity comes from the active session |
| `setRating` | mediaId, mediaType, integer rating from 1 to 10 | Confirmed rating; requires an active session |
| `videoQualities` | native video track facts; optional selected track ID in `select` | Sorted quality choices and validated `video` ID |
| `personalList` | kind: favorites/inprogress/done/planned/dropped | Media cards, with paging and mark grouping handled in Rust |
| `setFavorite` | mediaId, mediaType, selected | `{selected}` |
| `setMark` | mediaId, mediaType, status, current | `{mark}`; null after removal; Rust validates choices and chooses mutation |
| `setWatched` | entityId, selected | `{selected}` |
| `mediaKind` | mediaType | Content kind and valid mark keys |
| `episodeChoices` | mediaId, kind, resource (optional) | Playable items, kinds, selectedKind, resource IDs |
| `mediaList` | mediaType, offset, limit, filters | Page plus `nextOffset` and `hasMore` |
| `updatesPage` | type, offset, limit | `{docs,nextOffset,hasMore}` |
| `searchHistory` | action: list/add/remove; query | Current saved queries |
| `downloads` | none | `{root,items}` display snapshot |
| `downloadEnqueue` | kind: video/audio/manga/file; request | Download snapshot with stable ID |
| `downloadChange` | id, action: cancel/retry/delete | Updated item; null after delete |
| `downloadExport` | id, destination | `{path}` for the exported package |
| `playbackOpen` | url, episodeId, downloadId, episodeType; optional preferDub override | sessionId, intent, subtitlePaths, configDirectory, preferDub |
| `playbackReport` | sessionId, sequence, event, position, duration | `{seekTo}`; null means no seek |
| `selectTracks` | audio/subtitles track arrays, preferDub | Audio and subtitle IDs; null means no selection |
| `readerOpen` | slug, chapter, chapters (ordered chapter numbers) | `{chapter,previous,next}` |
| `clearCache` | none | Success after response-cache clear |
| `clearPlaybackAssets` | none | Removes prepared playback assets; requires no open playback session |

A download request includes mediaId/mediaType/slug/title and optional posterUrl.
Video/audio need episodeId and episodeUrl; episodeType supplies the default track
preference. Manga needs slug and chapter; chapterId/title/list are metadata. File
needs fileUrl and mediaId. IDs derive from kind and content identity. Repeated
enqueue returns the existing job. Failed jobs need an explicit retry.

The existing catalog, comments and API-shaped commands remain available through
the same application boundary. Successful personal mutations invalidate cached
responses, including calls through those API-shaped commands.

Profile changes accept only editable display fields and selected image paths.
The core takes the user ID from the session, validates both files before upload,
and limits each file to 10 MiB with PNG, JPEG, GIF, or WebP signatures. Name and
bio limits are 100 and 2,000 characters. Empty image URLs remove an image;
omitted fields are unchanged. Uploads and profile writes are not retried. The
backend returns the old profile after a write, so the core clears the cache and
reads the saved profile before updating session metadata.

Comments contain recursive `replies` arrays. The GraphQL query loads two reply
levels below each root, as on the website. The live server limits operation
depth to five; deeper replies are outside this query. The Windows view
places each loaded reply below its parent. New root comments omit `replyTo`
because this backend does not treat null as an absent parent. A reply sends the
selected comment ID. After sending, the UI reloads the server tree. It blocks a
second send while the first is pending and keeps the draft on failure. A new
comment cache key bypasses old cached responses that did not include replies.

Media details include the current user's `iRated` value separately from the
public `rating`. Both use the API's 10-point scale. `setRating` validates the
range, requires authentication, checks the backend status, and clears cached
responses after success. It does not retry writes. Windows converts scores to
five stars and rounds display values to the nearest half-star. A selected
half-star is one core rating point. The UI keeps the previous personal rating
if a save fails and blocks duplicate saves while a request is pending.

## Session and ordering

The active token stays in memory. Windows stores the login credential with DPAPI
in `Anibel/credentials.bin`. Rust owns the session revision and account metadata.
Session snapshots never contain the token. Token values are not stored in caches.

Reads share a request gate. Mutations, session changes, explicit reload, and cache
clear take the exclusive gate. A previous response cannot refill a cache after a
successful mutation clears it. Account expiry checks the revision used for the
request after it acquired the gate. An old error cannot log out a newer account.
Playback and reader history work carries the account revision captured at open;
it cannot add history to a different account after login changes.

History submission runs in the core without delaying a resume seek or page display.
Its failures are diagnostic events. History is not an offline sync queue.

## Cache

Keys include both endpoints, a token hash or anonymous scope, operation and args.
Only an explicit read allowlist is cached. Source resolution, random selection,
mutations and unknown operations are not cached. `cache: default` serves fresh
entries or stale entries while a single refresh runs. `cache: reload` waits for a
network result and reports failures to the caller.

| Reads | Fresh | Retained |
|---|---|---|
| Comments | 15 minutes | 2 days |
| Profile, personal lists and counters | 10 minutes | 2 days |
| Updates, slider and trends | 6 hours | 7 days |
| Other allowed catalog reads | 12 hours | 14 days |

`api-cache-v1.json` holds at most 512 entries and 16 MiB. Corrupt cache files are
discarded. Failed cache writes emit `cache.storageError` without failing a useful
network response. Explicit clear reports storage errors.

## Downloads and storage

Job states are `queued`, `downloading`, `completed`, `failed`, and `cancelled`.
There is one active job and at most four HLS segment transfers within it. Cancel
and delete wait for the worker to stop. Retry does not overlap the prior worker
and does not destroy a valid completed job. Invalid actions have no side effects.

Manifests use paths relative to the core directory. Managed paths reject parent
traversal and links. Restored assets must belong to the job folder. A completed
job is available only when its required files exist, including all HLS segments
and initialization data. Missing files produce a failed display state with retry
available. UI flags and absolute paths are derived; they are not persisted.

Supported offline media: direct files and finite, unencrypted TS/fMP4 HLS. HLS
keeps a local playlist and its segments. It is not concatenated into a pretend
MP4 file. Highest-bandwidth variants and relative URLs are resolved in Rust.
Separate HLS renditions, encryption, byte ranges, discontinuities, live/partial
playlists, and DASH downloads fail explicitly. Audio-only HLS requires a separate
audio source. Native online playback can still use formats supported by its engine.
Subtitle and font fetches are optional; unavailable fonts use engine fallbacks.

Export copies the whole package through a temporary directory, then renames it
inside the user-selected destination. Existing exports are not overwritten.
The destination must be outside core storage. Native code only supplies the OS
storage grant and path.

Bounds: 2048 jobs; 100,000 pages/segments per job; 32 GiB of completed assets per
job; 4 MiB HLS playlists; 256 MiB per segment; 64 MiB per manga image; 128 subtitle
files and 256 fonts per source. Durable JSON files are limited to 64 MiB. Individual
files and JSON snapshots use temporary files, sync, and atomic replacement.
Corrupt durable data is an error, not an empty library. Job restart repeats source
resolution and transfers. The Android adapter imports its former last-title selection once; other storage formats stay unchanged.

## Playback and reader rules

The native engine reports video track IDs, dimensions, bitrate, codec, image and
selection flags. Rust excludes image tracks, checks bounds and duplicate IDs,
sorts available qualities, and validates the requested ID. Windows uses native
MediaPlayer first, with FFmpegInteropX software decoding for unsupported video.
Quality replacement preserves position, pause, audio and subtitles. One engine
and the same controls serve the main player and PiP. Android uses Media3 and libass.

Core playback selects a valid local episode before resolving an online source.
An explicit download ID must identify a complete playable download. Rust prepares
subtitles and fonts, chooses default dub/sub behavior, and supplies engine inputs.
The native engine reports its actual track IDs to `selectTracks`.

At most eight playback sessions are open. Reports use increasing sequence numbers.
Old sequences and closed IDs have no effect. The first ready report restores a
position only when it is above 30 seconds and at least 15 seconds before the end;
it seeks five seconds earlier. Positions below five seconds are not saved. Saves
are limited to one per five seconds, with a forced save on close. End clears the
resume entry once; late position/close reports cannot restore it. At most 10,000
resume entries are stored. Failed saves do not commit the new session state.

Reader open selects complete local pages or fetches the chapter. It returns file
URLs for local images and the adjacent chapter numbers from the supplied list.
The native host renders these results and owns viewport position.

Windows owns a separate view for each open `(mediaType, slug)` title. Opening
another episode or chapter of the same title reuses that view. Different titles
can remain open at the same time. Each view can own one PiP window; docking or
closing that window affects only its title. The open-title menu can show, float,
or close each title. Window placement is native presentation state, not core
playback state. App shutdown closes all views and waits for playback reports.
Manga windows have no native title bar and no forced video aspect ratio. Their
window controls and reading toolbar appear on pointer movement or keyboard
focus. Controls stay visible during menu use. Only the top title area drags the
window, so pointer input over pages can scroll or zoom. The reading view offers
continuous scroll and single-page layouts, page/width fit, zoom, page selection,
and right-to-left arrow navigation. These are per-view presentation choices;
chapter selection, source selection, and history remain in Rust. Reader window
minimum sizes scale with display density, within the available work area.
Video windows use 16:9. A floating view must not hide a docked view.

## Snapshots and events

Events are notifications, not durable state. The queue holds 1024 events, keeps
the newest `session.changed`, and drops old diagnostic/cache notices when full.
Other notices are `cache.refreshed`, `cache.storageError`, and `error`.

The Windows host reads session and download snapshots every 500 ms without
overlapping polls. A host must read snapshots after reconnect/resume or missed
events. It must not reconstruct job or account state from events alone.

## Verification

Rust tests cover cache isolation/order, account expiry revisions, mutation choice,
search retention, track rules, paging bounds, download persistence and transitions,
HLS package integrity, resume bounds, late reports, failed writes, and ABI cancel.
Windows tests cover display projections, command forwarding, request scope,
engine event ordering, late seek rejection, and view behavior.

Run `cargo fmt --all --check`, `cargo clippy --workspace --all-targets -- -D warnings`,
`cargo test --workspace`, a release core build, then Windows tests and build.
Mock-server and DLL contract checks do not replace native rendering and lifecycle
tests. The Windows local fixture suite checks decoded frames, subtitles, seek,
quality and PiP. Android local device tests check JNI lifetime, execution permission,
rendered video, audio-track presence, seek and close. Required CI checks use local
fixtures; optional live checks are separate.
