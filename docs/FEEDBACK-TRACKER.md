# UI/UX Feedback Tracker (session 2026-08-31)

All items from the user's feedback during this session. Status updated as completed.
Legend: ✅ done · 🔄 in progress · ⬜ pending · ⚠ needs on-device verification

| # | Feedback | Status |
|---|---|---|
| 1 | Grid of title posters (not lists) on all catalog surfaces | ✅ `MediaCollectionView` (grid + row modes) |
| 2 | Media details page didn't work | ✅ FFI `media` arg fix + `live_media_details_by_slug` test; ⚠ clicking verified by user |
| 3 | All filters in catalog (year/status/language/genres) | ✅ per-type catalog filter panel |
| 4 | Switch between grid and list view | ✅ ▦/☰ toggles on Catalog + Search |
| 5 | Separate search page | ✅ sidebar Пошук page; ⚠ also wants toolbar search (see #11) |
| 6 | Episodes don't load / cannot watch | 🔄 retry + logs in place; ⚠ verify end-to-end incl. player |
| 7 | Manga: show chapters, not episodes | ⬜ details page must switch episode UI → chapters for manga/books |
| 8 | Russian names/descriptions should be Belarusian per settings | 🔄 Ui helper exists on cards/home; ⚠ details page still RU + genres untranslated |
| 9 | No empty states (no comments/episodes/…) | ⬜ add proper empty states |
| 10 | Skeletons instead of loading spinners | ⬜ add skeleton placeholders |
| 11 | Microsoft-Store-style top toolbar: search + account, native caption buttons | ⬜ TitleBar content (app events via WeakReferenceMessenger) |
| 12 | Pages should keep state; only explicit refresh reloads | ⬜ `NavigationCacheMode.Required` + guards |
| 13 | Infinite scroll loading | ⬜ `LoadMoreRequested` in MediaCollectionView + paging |
| 14 | Sidebar icons look non-native (emoji) | ⬜ native MDL2 glyphs (E80F/E721/E714/E8C3/E8B2/E7FC/E736/E77B/E713) |
| 15 | Home slider not WinUI-style | ⬜ rework to peeking/carousel with snap + auto-advance |
| 16 | Games catalog doesn't load | 🔄 CF 403 retry added; ⚠ verify |
| 17 | Sidebar back button doesn't work | ✅ `BackRequested` → `Frame.GoBack()` |
| 18 | Posters more rounded | ✅ 16px via Rectangle+ImageBrush |
| 19 | Comments missing avatars/PFP | ✅ rounded avatar + username/date/content |
| 20 | Search FFI `bad_args` (tuple parse) | ✅ field-based args + live test |
| 21 | Apps crash on opening media details | ✅ crash hooks + root causes fixed (missing Img resource etc.) |

## Verification notes
- Crash log: `%TEMP%\anibel-debug.log` (appends UNHANDLED/UNOBSERVED + page steps)
- Core: `cargo test -p anibel-core` + `cargo test -p anibel-core --test live -- --ignored`
- App: build `-p:Platform=x64`, core DLL copied to bin output manually after kill
