# Shared application core

Root AGENTS.md also applies. Read docs/SHARED-CORE-SPEC.md from the repository root before changing ownership or the command protocol.

- Keep shared policy in src/application. ffi.rs owns the C ABI and android.rs owns JNI adaptation.
- Native callers use application commands, not API adapters directly.
- Preserve protocol capability checks, response envelopes, request cancellation and buffer ownership. Every returned C buffer must be freed exactly once.
- Never unwind across FFI. Drain active requests before releasing shared resources.
- Use bounded queues and checked conversions for foreign sizes and IDs. Reject invalid paths and prevent writes outside managed storage.
- Keep atomic storage writes, cancellation and cleanup correct on failures. Do not log tokens or private response bodies.
- Preserve playback resume/history ordering and download progress/cancellation behavior. Keep user data separate from app binaries.
- Update contract tests and both native bindings for protocol changes. Avoid new crates or generic interfaces without a current dependency need.

From the root, run cargo test -p anibel-core and applicable workspace checks. Use local HTTP/media fixtures. Ignored live tests and FFmpeg integration tests have extra requirements; report separately whether they ran.
