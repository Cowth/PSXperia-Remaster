# Original PSXperia conversion engine — source audit

This audit is based on the user-provided `PSXperia-master.zip` and `PSXPeria-Wrapper-master.zip`. It is a technical reading of those sources, not a claim that the original tool has been ported.

## PS image container findings

The original `PSImage` implementation declares:

- A 20-byte fixed image header.
- `PART_SIZE = 0x9300` bytes per uncompressed segment.
- `PART_BS = 0x700` bytes as the stream copy buffer.
- Each segment is compressed independently using Java `DeflaterOutputStream` (zlib-wrapped DEFLATE by default).
- The TOC begins with `04 00 00 00`, then the uncompressed image length, a 12-byte constant, the segment count, and one little-endian 32-bit compressed-data offset per segment.

`PSImageCreate` writes each compressed segment consecutively and records the output stream position before writing that segment. These offsets and the zlib wrapper need byte-for-byte compatibility tests before the C# implementation can be considered correct.

## ZPAK findings

`ZpakCreate` builds a ZIP archive from a directory, omits hidden files/directories, uses relative forward-slash paths, and supports either DEFLATED or STORED ZIP entries. STORED entries require size, compressed size, and CRC32 to be populated. The default mode is compressed.

## Runtime/APK findings

`CrashBandicootExtractor`:

- Identifies a supported emulator APK by CRC32 and loads a matching patch configuration.
- Extracts APK and ZPAK content, applies XML/resource substitutions, verifies the emulator library checksum, and applies a binary patch where the selected config requires one.
- Replaces the emulator library with the PSXperia wrapper library.

`ApkBuilder` uses Android `aapt`/framework resources and signs the generated APK. The original code also contains a fixed signing keystore/password implementation; this must not be copied as a security design. A new implementation should generate and protect its own signing key, or use a user-managed key.

## Device wrapper findings

The supplied wrapper source includes Android JNI/C++ code and ARM assembly patches tied to particular emulator library versions. It is device-side native code, not logic that can be replaced by a C# Windows class. The Windows app can package and deploy the compatible native library, but each patch/config pair must be verified against the exact runtime binary checksum and Xperia Play firmware/runtime combination.

## Licensing and asset separation

Several original Java files contain GPL-3.0-or-later headers. The supplied archive does not expose a root LICENSE file in the inspected listing, and the wrapper archive did not expose a LICENSE/COPYING file. Before copying or distributing derived code, verify the complete repository, submodule, and dependency licensing and comply with applicable notices/source-offer obligations. The Sony emulator APK/ZPAK/runtime assets are separate proprietary assets; the user's ownership of a PS1 disc does not grant redistribution rights to those files. Keep user-supplied runtime assets local and out of our source ZIPs.

## Safe implementation order

1. Port the PS image writer and build a round-trip/byte-layout test against the original implementation.
2. Port the ZPAK writer and compare ZIP entry names, compression methods, sizes, and CRCs.
3. Build a runtime extraction/validation workflow that accepts user-provided compatible assets without bundling them.
4. Port region/version config selection and binary patching only after checksum fixtures are available.
5. Build APK packaging/signing with a user-managed signing key and validate installability on the Xperia Play.
6. Deploy the companion data folder and verify the wrapper's TOC path and Android permissions.
