# Milestone 03.1 — Library I/O and region detection

## Fixes
1. Removed automatic disk writes from `LoadAsync`. Previously, opening or refreshing the app could normalize old entries and immediately overwrite `library.json`. A read-only attribute or Windows permission restriction could then cause the app to report `Access to the path is denied`.
2. Save operations now use a uniquely named temporary file, clear the destination's Read-only attribute when necessary, and replace the library only after serialization succeeds.
3. Legacy CUE/track grouping is best-effort and occurs in memory during load. Missing or inaccessible source images do not prevent the library from opening.
4. Region detection checks filename labels and common PlayStation product-code prefixes.

## Important
- Keep the original `library.json` as a backup before testing.
- This is not yet a conversion engine. No APK/ZPAK is built and no Xperia installation is performed.
- If Windows still denies access, the improved exception text identifies whether the library file or destination folder is blocked.
