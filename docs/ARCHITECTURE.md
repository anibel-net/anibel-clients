# Architecture

Rust owns shared application policy. Windows and Android own native presentation and OS integration.

## Dependencies

```text
Windows / Android -> anibel-core (application + protocol + ABI)
                       -> anibel-api -> anibel-domain
                       -> anibel-player -> anibel-domain
```

All Rust packages are in `crates/`. `anibel-core` exports `anibel_core` through C ABI and JNI. Its package name, ABI and data directory do not depend on the source folder name. `anibel-player` resolves sources; native engines play them.

## Owners

| Owner | Responsibility |
|---|---|
| Rust Application | Commands, session revision, cache rules and shared feature coordination |
| Rust protocol | Command identity, read/write classification, cache and cancellation policy |
| Rust Downloads | One queue owner; durable records, cancellation, execution permission and cleanup |
| Rust Playback | Resume/history and ordered playback reports |
| C ABI / JNI | Handle lifetime, request reservation, cancellation and buffer ownership |
| Windows App / OpenMediaWorkspace | Services, shutdown and open media windows |
| Windows PlayerController / WindowsMediaEngine | Core playback session / native playback timeline |
| Android Application | Process-lifetime core and platform services |
| AndroidPlaybackSession | One native player, subtitle renderer and ordered core session |
| Android DownloadService | Background execution permission; resume and drain Rust workers |
| Native feature state | Requested selections, loading/error display and navigation |

Use modules and feature folders before adding build units. Keep one mutable owner per resource. Derive display state; persist only facts needed after restart. Mutually exclusive command and job states use enums. Shared behavior belongs in Rust; focus, layout, media surfaces and system lifecycle stay native.

The gate around cached reads and mutations prevents stale requests from restoring invalidated state. Non-cached read commands use the read gate. Explicit reload keeps the write gate. Cancellation must not drop an accepted backend mutation.

## Contracts and validation

- [Core protocol and storage](SHARED-CORE-SPEC.md)
- [Windows ownership](../apps/windows/ARCHITECTURE.md)
- [Windows release process](../apps/windows/RELEASING.md)
- [Android ownership](../apps/android/ARCHITECTURE.md)
- [Android build and device checks](../apps/android/README.md)

CI checks all Rust packages, Windows native playback and release update packages, and Android build, lint, JVM contracts and local emulator playback. Device tests are separate from assembly and package validation. Windows 10 runtime support needs device validation; an SDK minimum is not proof.

Historical designs and completed feedback are available in Git history. They are not a second source of current implementation rules.
