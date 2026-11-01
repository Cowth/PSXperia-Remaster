# Milestone 06.1 — Compile fix

## Fix

Resolved compiler error CS1674 in `PSXperia.Engine/RuntimeValidator.cs`.
`Crc32Stream` is a managed helper with no disposable resources, so it is now instantiated normally rather than used in a `using` statement. The CS0006 missing-metadata errors shown in Visual Studio were downstream consequences of the Engine project failing to compile.

## Verification

- Inspected the exact source line reported by the user's Visual Studio screenshot.
- Confirmed `Crc32Stream` owns only a uint field and a static lookup table; it has no stream, handle, or other disposable resource.
- Confirmed the invalid `using` declaration is removed.
- .NET compilation could not be run in the build environment because the .NET SDK is not installed. Please run **Build > Rebuild Solution** in Visual Studio and send any remaining errors.
