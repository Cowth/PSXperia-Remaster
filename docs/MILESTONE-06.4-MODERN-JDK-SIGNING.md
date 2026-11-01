# Milestone 06.4 — Modern JDK APK signing

## Purpose

Remove the original 2011 `SignedJarBuilder` signing path, which crashes on modern JDKs because it relies on the removed `sun.misc.BASE64Encoder` class. Preserve the original PSXperia extraction, game patching, PS image generation, TOC generation, ZPAK packaging, and AAPT resource packaging.

## Changes

- The bundled PSXperia JAR still performs the original extraction, runtime patch selection, image conversion, ZPAK creation, and legacy AAPT APK packaging.
- `ApkBuilder` now stops after AAPT creates the unsigned APK (`<game-apk>.unsigned`). It no longer invokes the obsolete `SignedJarBuilder` or references its removed Base64 implementation.
- The WPF engine signs the unsigned APK using the `keytool` and `jarsigner` shipped with a full JDK 21.
- A 2048-bit RSA signing key is generated once in `%LOCALAPPDATA%\PSXperiaRemastered\signing\psxperia-release.p12` and reused for subsequent game builds, allowing same-package updates from this Windows installation.
- The generated signature is JAR/V1 only. No APK Signature Scheme V2/V3 block is added, to retain compatibility with the Xperia Play's older Android release.
- The signing password is generated randomly and stored beside the local keystore. Back up both files together if preserving the ability to update installed games is important. The private key is not bundled into the app ZIP.
- Final validation still checks the APK manifest and DEX, game ZPAK metadata and image payload, and TOC. Signing is verified with `jarsigner -verify` before publishing the output folder.

## Output layout

```text
PSXperia-<TITLE_ID>/
  com.sony.playstation.<TITLE_ID>.apk
  data/
    com.sony.playstation.<TITLE_ID>/
      files/
        content/
          <TITLE_ID>.zpak
          image_ps_toc.bin
```

## Toolchain requirement

A full JDK 21 is required, not a JRE. `JAVA_HOME` should point to the JDK installation directory and its `bin` directory must contain `java`, `keytool`, and `jarsigner`.

## Verification performed for this milestone

- Recompiled the modified `ApkBuilder.java` using OpenJDK 21 against the original PSXperia JAR.
- Replaced the bundled `ApkBuilder.class`; inspected the bytecode to confirm the class no longer references `SignedJarBuilder`, `BASE64Encoder`, or a `signApk` call.
- Started the patched PSXperia CLI under OpenJDK 21 and confirmed its usage screen loads.
- Ran an independent signing smoke test using JDK 21 `keytool` and `jarsigner`: generated a PKCS#12 RSA-2048 key, signed a sample APK-shaped ZIP with SHA256withRSA/SHA-256, and successfully verified the signature.
- Full WPF compilation and end-to-end conversion against the user's large runtime ZPAKs and PS1 disc remain to be tested on the user's Windows machine. This milestone is not represented as device-tested.

## Important compatibility note

The original 2011 converter's shared signing keystore uses a 1024-bit DSA/SHA-1 key. This milestone intentionally replaces it with a locally generated RSA-2048/SHA-256 key. Android package updates require the same signing key as the already-installed copy. If the user previously installed an APK signed by the original converter, Android may require uninstalling that copy before installing the newly signed build; uninstalling may remove app-local save data. Back up saves first. All future builds signed with this Remastered installation's local key can update one another normally.
