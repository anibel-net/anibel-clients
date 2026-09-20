# Windows UI checks

Parent guidance applies. Use ANIBEL_APP_EXE to select the test build. UI automation requires a Windows desktop session. Use stable automation IDs and bounded waits; do not hide assertion failures with unconditional delays. Keep test actions read-only unless they use isolated test data. Report unavailable desktop access as a limitation, not a passed check.
