# Repository guidance

## Scope and structure

These rules apply to the whole repository. Read the nearest project AGENTS.md before editing. The root README is Belarusian only; code identifiers and technical guidance may use English.

- `crates/anibel-core`: shared application policy, state, storage, C ABI and Android JNI.
- `crates/anibel-domain`: shared data models and errors.
- `crates/anibel-api`: GraphQL requests and response mapping.
- `crates/anibel-player`: video service and source resolution.
- `apps/windows`, `apps/android`: native UI and OS integration.
- `scripts`, `tools`: build, release and validation support.

## Design review

Before each change:
- Derive values from existing facts instead of adding stored state where possible.
- Remove or merge unnecessary branches, structures and classes.
- Use enums for closed sets of states.
- Prefer one clear implementation over parallel implementations of the same rule.
- Keep the change within the requested scope.
- Check arithmetic, lengths, offsets, indices and capacity bounds.
- Introduce a new abstraction only when the current change needs it.

Rust owns shared application behavior. Native hosts own UI state, playback engines, secure credentials and OS integration. Do not duplicate core business rules in native clients. Update both bindings and contract tests when changing the protocol. Build the core and clients together.

## Work and safety

- Preserve unrelated local changes. Do not reset, clean or rewrite history without explicit instruction.
- Never commit credentials, signing keys, local SDK paths, generated binaries, caches or downloaded media.
- Do not print tokens or signed URLs in logs or test output.
- Keep source and release dependency notices. Our MIT license does not replace third-party licenses.
- Do not change repository visibility, push, tag or publish unless the user asks.
- Keep explanations short and use simple English. State checks run and any checks not run.

## Validation

Run checks that match the changed code. From the root, Rust checks are:

```sh
cargo fmt --check
cargo clippy --workspace --all-targets -- -D warnings
cargo test --workspace
```

Use local fixtures for automated tests. Live tests must be explicit and must not change production account data. Do not claim device or UI verification from a successful compile. See project guidance for native checks.

## Documentation and releases

Keep README commands accurate. Update documentation when behavior or build steps change. Windows release tags use `windows-vX.Y.Z` or `windows-vX.Y.Z-beta.N`. Tags create drafts; do not publish or replace an existing release without authorization. Keep user data outside the installation directory.
