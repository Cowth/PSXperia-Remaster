# Visual Studio Development Handoff

This package is for local development and testing. It does not establish
redistribution rights for the bundled Java converter or any proprietary runtime
files.

## Development environment

Observed and verified in the handoff build environment:

- Windows x64.
- Visual Studio Community 2026, version `18.11.12328.437`, with the **.NET
  desktop development** workload (`Microsoft.VisualStudio.Workload.ManagedDesktop`).
- .NET SDK `10.0.403` (the projects target .NET 10).
- Java Development Kit 21 for the legacy Java converter and external APK
  signing. The verification environment had Eclipse Temurin `21.0.13+9-LTS`.
- Android SDK Platform-Tools (`adb`) on `PATH` for device features. `adb` is
  not needed for the managed solution build or test suites.

The solution file records `VisualStudioVersion = 18.11.12328.437`. This records
the solution authoring environment; it does not specify a minimum Visual Studio
version. Use an IDE installation that supports the .NET 10 SDK and WPF desktop
projects. The package does not claim that an earlier Visual Studio version has
been tested.

## Restore, build, and tests

In Visual Studio, open `PSXperia.Remastered.sln`, restore NuGet packages, select
**Release** and **x64** in the configuration toolbar, then choose **Build
Solution**.

From a Visual Studio Developer PowerShell, the verified environment can also
build the full solution with:

```powershell
MSBuild.exe .\src\PSXperia.App\PSXperia.App.csproj /t:Restore /p:Configuration=Release /p:Platform=x64
MSBuild.exe .\tests\PSXperia.Engine.Tests\PSXperia.Engine.Tests.csproj /t:Restore /p:Configuration=Release /p:Platform=x64
MSBuild.exe .\tests\PSXperia.RuntimeArchiveMeasure.Tests\PSXperia.RuntimeArchiveMeasure.Tests.csproj /t:Restore /p:Configuration=Release /p:Platform=x64
MSBuild.exe .\PSXperia.Remastered.sln /t:Build /p:Configuration=Release /p:Platform=x64 /verbosity:minimal
dotnet test .\tests\PSXperia.Engine.Tests\PSXperia.Engine.Tests.csproj -c Release --no-build
dotnet build .\tools\PSXperia.RuntimeArchiveMeasure\PSXperia.RuntimeArchiveMeasure.csproj -c Release --no-restore
dotnet run --project .\tests\PSXperia.RuntimeArchiveMeasure.Tests\PSXperia.RuntimeArchiveMeasure.Tests.csproj -c Release --no-build
```

The engine suite is an xUnit test project. The Runtime Archive Measurement
regression harness is a console program, so run it with `dotnet run`; it is not
an xUnit/VSTest project. Both suites use synthetic fixtures and do not establish
Sony runtime or Xperia Play compatibility.

## Local conversion prerequisites

The managed build and synthetic tests do not need Sony runtime files, game
images, signing secrets, or an attached phone. To perform a conversion, install a
full JDK 21 and configure `JAVA_HOME` so `java`, `keytool`, and `jarsigner` are
available. Select legally obtained, locally stored Sony runtime APK/ZPAK files
in the application. Add Android Platform-Tools to `PATH` only for ADB/device
features.

Signing material is created and retained locally under
`%LOCALAPPDATA%\PSXperiaRemastered\signing`. Do not add that directory, the
keystore, certificates, or password files to this repository or development ZIP.
Keep backups local and protected. This package contains no signing credentials.

## Frozen Java engine and outstanding work

The existing `tools/PSXperiaTool.jar` is frozen at SHA-256
`01857E1615DC4514F6DB65DABB8CA93ED8F893CCC1BD5307C59B21504500FBF8`. Do not
rebuild or replace it as part of this handoff. Its source/build inputs are not
verified as reproducible, and the exact modified Java source for unsigned APK
generation and Crash Bandicoot no-patch handling has not been recovered.

The following security work remains outstanding:

- Java ZIP extraction path containment in `CrashBandicootExtractor.extractZip`.
- APKTool 1.4.1 resource extraction path containment and resource-processing
  limits.

The current engine has not been hardened by this handoff. Treat Java extraction
security as unresolved until implementation and focused regression testing are
completed.

Licensing and provenance are distribution blockers. The recovered PSXperia
source carries GPL-3.0-or-later headers, while bundled/repository notices make
conflicting Apache references. The current JAR has not been proven reproducible
from the pinned sources and inputs. Do not infer redistribution rights from
public availability or the presence of a license file; obtain legal review and
resolve corresponding-source and third-party notice obligations before
redistribution.

The package excludes the raw Step 1B recovery checkouts. In particular, the
upstream source checkout contains a tracked signing keystore and must not be
shared as a sanitized source package.
