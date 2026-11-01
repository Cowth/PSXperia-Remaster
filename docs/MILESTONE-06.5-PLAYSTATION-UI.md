# Milestone 06.5 — PlayStation UI Refresh

## Changes
- Uses the user-supplied PlayStation wave wallpaper as a subtle, darkened workspace background. The supplied GIF contains one frame, so it is packaged as a PNG resource rather than described as animated.
- Converts the converter from a separate modal Window into an in-app workspace view hosted by MainWindow. The converter includes a Dashboard navigation action.
- Fixes the file-picker text contrast: the nested path TextBoxes now explicitly use a dark field background and white foreground, rather than inheriting the Windows light TextBox theme.
- Adds shared readable TextBox and ComboBox styling and slightly increases muted text contrast.
- Leaves conversion engine, runtime validation, Java signing, APK generation, ZPAK generation, and TOC generation unchanged.

## Verification
- Source-level review performed for the view transition, owner-bound file dialogs/message boxes, WPF resource path, and field contrast.
- The provided wallpaper is 498 × 287 pixels and has one frame.
- ZIP archive integrity checked after packaging.
- This environment does not have the .NET 10 SDK / MSBuild, so a WPF compile and runtime smoke test could not be performed here. Build in Visual Studio before replacing the known-good 06.4 build.
