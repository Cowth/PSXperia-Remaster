# Milestone 01 — Disc image import

## Implemented
- CUE parser for FILE / TRACK / INDEX 01.
- Resolves relative track paths and checks each referenced file exists.
- Basic raw image sector alignment checks.
- JSON-backed local library.
- Dashboard list populated from saved imports.

## Known limitations
- No full PlayStation filesystem / sector validation yet.
- No multi-disc grouping, subchannel decoding, track extraction, or conversion.
- A standalone BIN file cannot reliably reveal track layout; prefer importing its CUE sheet.
- Some CUE variants may use unquoted FILE paths with spaces; use quoted paths.
- This is an inspection/import milestone, not proof that a disc image will convert or run.
