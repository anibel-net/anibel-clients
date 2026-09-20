# Video source adapter

Root AGENTS.md also applies.

Resolve video service responses into shared playback sources and track data. Do not implement native rendering or UI state here. Resolve relative media, subtitle and font URLs against the correct service host. Preserve source track names and separate audio/subtitle choices. Do not assume different episode uploads share a timeline. Do not log signed media URLs. Use local HTTP fixtures for manifest and URL tests. Run cargo test -p anibel-player and affected core playback tests from the root.
