# Shared domain models

Root AGENTS.md also applies.

Keep this crate independent of UI, transport and storage. Use explicit enums for closed sets and keep serialization contracts stable. Check numeric bounds and defaults. Do not put platform display text or network calls here. When changing a public model, check all Rust users, Windows DTOs, Android mappings and contract tests. Run cargo test -p anibel-domain and affected workspace tests from the root.
