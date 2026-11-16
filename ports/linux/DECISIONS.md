# Linux port decisions

Keys are appended by the task that produces them.

## ESSENTIALS_GAPS

Found when the app first ran on Linux (Task 5), under OpenMaui.Controls.Linux 10.0.130.1:

- `AppInfo` and `FileSystem` are NOT implemented when `MauiProgram.CreateMauiApp()` starts: `AppLog.Install()` (first statement of the composition root) reads `AppInfo.Current.VersionString` and `FileSystem.AppDataDirectory` and throws `NotImplementedInReferenceAssemblyException`. OpenMaui installs its own `FileSystem` (XDG data/config folder named `PKForge`) and `Preferences` (`$XDG_CONFIG_HOME/PKForge/preferences.json`) later, during `LinuxApplication.Run`.
- Decision: the head installs `LinuxAppInfo` and `LinuxFileSystem` from `Program.Main` before composing (`Hosting/EssentialsFallbacks.cs`, via reflection on the internal `SetCurrent`). `LinuxFileSystem` uses the same folder names as OpenMaui so early and late logs share one folder. `AppInfo` stays ours for the whole run; since Task 8 its version (`3.2.0`) and build (`320`) come from the csproj's `ApplicationDisplayVersion`/`ApplicationVersion`, stamped into the assembly as `AssemblyMetadata` and read back by `LinuxAppInfo` (the app logs `Started · PKForge 3.2.0`).
- `Preferences` and `DeviceDisplay` needed no override to reach Home. No `NotImplementedInReferenceAssemblyException` appears in `pkforge.log` or stdout after startup. Not yet exercised: `Permissions`, `Connectivity`.
- Task 8 timing check: `LinuxEssentials.Install()` (Share, FilePicker, Clipboard, Launcher, HapticFeedback via the non-public `SetDefault`) runs right after `UseLinux()`. Whether `LinuxApplication.Run` overwrites it was checked at run time: `LinuxHosting` logs `Xxx.Default`'s type once the first window exists and again 5 s later. Both times all five resolved to the head's own types (`PKForge.Linux.LinuxShare`, `LinuxFilePicker`, `LinuxClipboard`, `LinuxLauncher`, `LinuxHaptics`), so the early install sticks for these five (OpenMaui's late installs cover other services). The probe stays in as a regression tripwire; if a later OpenMaui bump overwrites one, the log line names the type that won.
- `SetCurrent` (AppInfo, FileSystem) and `SetDefault` (the five above) share one reflection helper, `EssentialsFallbacks.SetStatic`.

## RENDER_GAPS

Checked by eye on the Xvfb screenshot (Task 5):

- `RoundRectangle`/rounded borders: render correctly (dialog box, dock tiles, "Filter" chip, battery pill).
- Gradient brushes: render correctly (Task 8, undimmed Home screenshot). The dock tiles shade top to bottom (sampled column through the Bank tile: rgb 36,44,78 at the top edge down to 25,34,67 at the bottom); the panels and chips use the same brush.
- Stale frame after the box browser's Back (open, not fixed; the code is in `src/` and OpenMaui): open a save with "Open a single save file" and the app pushes `BoxBrowserPage`. Pressing B pops to `HomePage` (the navigation stack and the pad router agree, the log shows `top=HomePage`), but the window keeps showing the box browser's last frame for as long as we waited (15 s), also after `LinuxApplication.RequestRedraw()`. Popping the head's own `FileBrowserPage` back to Home repaints fine, animated or not, so it is specific to leaving `BoxBrowserPage` (its `SettleThenAsync(() => Navigation.PopAsync())`). smoke.sh therefore ends on the folder-link flow, which never opens the box browser.
- Window is 833x625 inside the 1024x768 display (OpenMaui default window size at 1.04 scale), not full-screen. Software GL (llvmpipe) is used under Xvfb; no `OPENMAUI_RENDERER=raster` needed.
