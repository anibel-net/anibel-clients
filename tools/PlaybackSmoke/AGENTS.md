# Native playback checks

Parent guidance applies. Read README.md before running. NativePlaybackSmoke.cs is compiled into the Windows app only when EnablePlaybackSmoke is set. Use generated fixtures from create-fixtures.py; do not commit media. Keep assertions for rendering, seek completion, buffering, clock agreement, track choices and PiP lifecycle. Fail when an expected check does not run.
