# Windows app

Root AGENTS.md also applies. Read ARCHITECTURE.md for lifecycle ownership and RELEASING.md for packaging.

## Implementation

- Use native WinUI 3 controls and existing theme resources. Do not replace native navigation or rating controls with custom templates without a concrete need.
- Keep UI updates and observable collections on the UI thread. Run the blocking C ABI off that thread.
- View models own display state. CoreClient is the core command boundary; Rust owns downloads, resume data, history and source policy.
- App owns services. OpenMediaWorkspace owns open players/readers and detached windows. PlayerController owns playback sessions; WindowsMediaEngine owns native playback and subtitle resources.
- Cancel replaced operations, ignore stale callbacks and await cleanup before freeing native handles. Keep shutdown order explicit.
- Seeking must update requested UI position immediately. Buffering must show loading state without overriding the user's pause choice. Check separate audio and video clocks.
- libass layout must account for DPI, video bounds and fallback fonts. Keep media centered on the first layout.
- Use protected credential storage. Do not log tokens, private profile data or signed media URLs.
- Use existing Belarusian terminology. Search fields must not take focus on navigation or player close unless the user requested search.

## Checks

From the repository root:

```powershell
dotnet test apps/windows/tests/Anibel.App.Tests.csproj -p:Platform=x64
dotnet build apps/windows/src/Anibel.App.csproj -c Debug -p:Platform=x64
```

For playback changes, follow tools/PlaybackSmoke/README.md and run the local native playback suite. Check seek, quality, subtitles, buffering, separate audio and PiP when affected. For visual changes, inspect the app where available; report if visual checks were not possible.

Use a fresh publish folder. Keep ReadyToRun and required WinUI/interop assemblies unless measurements and tests support a change. Do not change the Velopack package ID or user data location. Unsigned builds are intentional. Full/delta package validation alone does not prove an installed update works.
