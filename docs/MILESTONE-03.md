# Milestone 03 — Library grouping and region repair

- Treats a CUE sheet and all BIN/IMG files referenced by it as one disc/library entry.
- Repairs existing libraries on load by removing separately imported track-file entries already represented by a CUE entry.
- Preserves a cover image from an old duplicate track entry when the CUE entry has no cover.
- Rebuilds track metadata from the CUE sheet so audio/data track counts and INDEX 00/01 remain available.
- Backfills region metadata for older entries and expands filename/product-code recognition for USA, Europe, and Japan.

This milestone does not implement PS1 conversion. It does not yet merge separate Disc 1 / Disc 2 CUE sheets into a single multi-disc game container; each CUE remains one disc entry. A future multi-disc game model should group those disc entries under one title while retaining each disc's own CUE and tracks.

\n## Access-denied import handling update
- CUE import no longer fails only because Windows denies reading a referenced track's file-size metadata.
- The library can record the CUE and its track references while showing a warning that the track must be readable before conversion.
- This does not bypass Windows security or make an unreadable BIN usable by the conversion engine. A later conversion read must succeed.
- Missing referenced files remain a blocking CUE validation error.

\n## Access and region diagnostic patch
- Reads CUE sheets and referenced tracks with shared read-only access.
- Provides a clearer exception if Windows denies access to the CUE itself.
- Expands region recognition for common PS1 product-code prefixes and region labels in the full path.
- Import/metadata only; this does not alter or implement PSX conversion.
