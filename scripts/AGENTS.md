# Build and release scripts

Root AGENTS.md also applies.

Resolve paths from the script location. Stop on external command failures. Validate a resolved target before recursive deletion. Keep builds in ignored output directories and publish into a fresh directory. Pin downloaded source versions and verify known checksums where available. Copy required license notices with runtime files. Never embed credentials. Keep release versions, channels and filenames consistent with apps/windows/RELEASING.md. Validate PowerShell syntax and shell syntax after script changes, then run the affected build or packaging step when practical.
