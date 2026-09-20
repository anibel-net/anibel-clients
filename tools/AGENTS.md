# Validation tools

Root AGENTS.md also applies. These tools validate the product; do not move production behavior into test helpers.

Keep media fixtures local and generated. Use temporary directories with clear ownership and cleanup. Keep production smoke hooks excluded from normal release builds. Check observable results: rendered frames, clock agreement, restored state and downloaded package hashes. A successful process exit alone is not enough. Do not let a full-package fallback hide a failed delta test. Document required native dependencies, fixture commands and limits. UI/device tests, package staging tests and installed-update tests are different; report which actually ran.
