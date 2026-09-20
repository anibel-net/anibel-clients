# Android ownership

The Application owns one CoreClient per data directory and the image cache. CoreClient owns JNI requests and credentials; screens do not call GraphQL.

CoreCommand defines wire identities. Feature code uses the binding, while Rust owns cache, source selection, queue and resume/history policy. UI state and phone/TV layouts remain native.

AndroidPlaybackSession owns ExoPlayer, libass and one core playback session. Its run operation releases native media and sends the final ordered close report in a non-cancellable cleanup section. PlayerScreen owns only the surface, controls, activity chrome and lifecycle attachment.

DownloadService grants Rust execution time using downloadsResume. Its cleanup calls downloadsSuspend and waits for worker cleanup, preserving queued records. Cancelling the service's polling coroutine alone is not sufficient. Android starts with downloads suspended. The service and screen are separate owners.

Build and lint checks run in CI. Playback and lifecycle changes also require phone and TV instrumentation checks. Do not claim device behavior from a successful APK build.

Source folders group app composition, the core binding, navigation, features,
platform adapters and shared UI. Kotlin/JNI names stay stable. Closed commands use
CoreCommand; playback inputs and reports have typed adapters. CoreClient.close
rejects new requests, drains existing calls, and releases its native handle once.
The application keeps its core for the process lifetime; isolated tests close theirs.

Rust owns last-episode and last-chapter selection. Native preferences retain page,
scroll offset and reading mode only. The former title selection is imported once.

CoreSnapshots shares session and download snapshots between subscribers. It polls
only while a screen or service observes them, and slows down for idle downloads.
Closing CoreClient cancels the observers before it drains native calls.
