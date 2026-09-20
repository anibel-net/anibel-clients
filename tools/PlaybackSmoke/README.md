# Native playback smoke test

Build the small FFmpeg runtime and fetch the MSYS2 libass runtime first (see the
root README). Use a full FFmpeg build only to generate fixtures:

```powershell
python tools/PlaybackSmoke/create-fixtures.py --ffmpeg C:/path/to/full/ffmpeg.exe
dotnet build apps/windows/src/Anibel.App.csproj -p:Platform=x64
```

In a separate terminal, serve only the fixtures on loopback:

```powershell
python -m http.server 18765 --bind 127.0.0.1 --directory artifacts/native-playback-test
```

Run the smoke mode (enabled by default in Debug, absent from normal Release):

```powershell
apps/windows/src/bin/x64/Debug/net10.0-windows10.0.22000.0/win-x64/Anibel.Net.exe --native-playback-smoke artifacts/native-playback-test
```

The test opens a window and writes `artifacts/native-playback-test/result.txt`.
Success ends with `ALL PASSED`. It exercises the real Windows media engine and
libass using MP4, HLS, DASH, separate audio, and MKV with embedded subtitles/fonts.
It checks the published title-bar logo URI, clock progress, subtitle pixels, track switching, volume/mute, pause,
seek, resizing, and moving to a PiP window and back. Adaptive stream checks verify
the highest default quality and decoded resolution after lower/higher selections,
with playback position, pause state, audio, and subtitles preserved.
It also checks Rust-style verbatim file paths, UTF-8 MKV track names, missing-font
fallback, and subtitle raster/layout sizes across 100/125/150/200% DPI. Calls use
the shipped Rust core with isolated storage under the test directory; the test
does not read a user's account or library.
Stop the HTTP server when finished. Repeat on Windows 10 before claiming support.

For a live test, put public `https://video.anibel.net/<id>` episode URLs in a
separate test directory's `sources.json` array. These use the real `playbackOpen`
operation, subtitle/font downloads, and track selection. Add `#dub` to a test URL
to check dub and signs preferences. The live run also loads the actual PlayerHost
control and checks its settings menu, mini mode, rapid pause/resume, immediate seek
dispatch, preserved button focus, and close. Streams without
subtitles do not satisfy this suite's subtitle assertions.

The suite needs a Windows desktop session to create its XAML surfaces, but needs
no mouse or keyboard input. It verifies audio track selection, not audible sound
quality or lip sync. Seek checks wait for the requested clock position before
checking real subtitle pixels; network buffering can delay a seek.

For package testing, publish to a separate test output with
`-c Release -p:EnablePlaybackSmoke=true --self-contained true`. This includes the
same native libraries as the normal package. Run that output with the same smoke
arguments. Do not use this opt-in property for the build shared with users.

To check the shell without media, use a directory with `sources.json` containing
`[]`. This checks Solid, Mica, Acrylic, and search focus when controls disappear.
It also checks that an explicit request can still focus search. Background choices
are changed only in memory; the user's settings file is not changed.
This run also loads the catalog, search, title, profile, profile-list, and downloads
pages, renders a chapter row, and checks WebView2. It reports shell-load and
process-to-shell times. Compare repeated alternating runs on the same machine;
exclude the first warm-up run from the median.

For optimized/untrimmed comparisons, use separate `IntermediateOutputPath` and
publish folders for each configuration to avoid stale dependency manifests.
For example, use `-p:IntermediateOutputPath=obj/optimized-smoke/` with
`-p:OptimizeDistribution=true`, and `obj/baseline-smoke/` with `false`.

High 10 H.264 fixtures check automatic software fallback for MP4, HLS, DASH,
and separate audio. Ordinary H.264 stays on Windows decoders. Software quality
checks use fresh single-quality manifests and keep audio, subtitles and seek state.
The shell check also verifies bottom-right update notices and the restart button
with a fake updater (including a launch failure); it does not apply an installed update.
