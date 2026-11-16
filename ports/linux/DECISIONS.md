# Linux port decisions

Keys are appended by the task that produces them.

## ESSENTIALS_GAPS

Found when the app first ran on Linux (Task 5), under OpenMaui.Controls.Linux 10.0.130.1:

- `AppInfo` and `FileSystem` are NOT implemented when `MauiProgram.CreateMauiApp()` starts: `AppLog.Install()` (first statement of the composition root) reads `AppInfo.Current.VersionString` and `FileSystem.AppDataDirectory` and throws `NotImplementedInReferenceAssemblyException`. OpenMaui installs its own `FileSystem` (XDG data/config folder named `PKForge`) and `Preferences` (`$XDG_CONFIG_HOME/PKForge/preferences.json`) later, during `LinuxApplication.Run`.
- Decision: the head installs `LinuxAppInfo` and `LinuxFileSystem` from `Program.Main` before composing (`Hosting/EssentialsFallbacks.cs`, via reflection on the internal `SetCurrent`). `LinuxFileSystem` uses the same folder names as OpenMaui so early and late logs share one folder. `AppInfo` stays ours for the whole run (`1.0.0`, build `1`); Task 8 should replace it with real values.
- `Preferences` and `DeviceDisplay` needed no override to reach Home. No `NotImplementedInReferenceAssemblyException` appears in `pkforge.log` or stdout after startup. Not yet exercised: `Share`, `Launcher`, `HapticFeedback`, `Clipboard`, `FilePicker`, `Permissions`, `Connectivity` (add to Task 8's list if they throw).

## RENDER_GAPS

Checked by eye on the Xvfb screenshot (Task 5):

- `RoundRectangle`/rounded borders: render correctly (dialog box, dock tiles, "Filter" chip, battery pill).
- Gradient brushes: not verifiable. The first-run dialog dims the Home screen, and the screenshot shows only flat fills and the grid; re-check once the first-run overlay can be dismissed without a crash (needs Task 6 to 12 services).
- Window is 833x625 inside the 1024x768 display (OpenMaui default window size at 1.04 scale), not full-screen. Software GL (llvmpipe) is used under Xvfb; no `OPENMAUI_RENDERER=raster` needed.
