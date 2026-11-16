# PKForge on Linux (RG DS Plus): spike results and port guide

Last updated: 2026-11-15. Spike run on x86_64 under a virtual X display, **not** on the device.

## 1. Goal
Run PKForge (github.com/sofianeelhor/pkforge, an Android Pokémon save editor and Bank) on the Anbernic RG DS Plus. That device is Linux-only: Rockchip RK3568, 1 GB RAM, two 4.3" 1024×768 touch screens, no Android build.

## 2. Findings from reading the code
| Layer | Lines | Target | Portable? |
|---|---|---|---|
| `PKForge.Domain` | ~21k | net10.0 | Yes. Platform contracts live here. |
| `PKForge.Engine` | ~37k | net10.0 | Probably (depends on the PKHeX submodule; not built in this spike). |
| `PKForge.Infrastructure` | ~4k | net10.0 | Yes. |
| `PKForge.Chrome` | ~15k | net10.0 | Yes. About 50 pure-SkiaSharp `*Paint` classes. |
| `PKForge.App` | ~60k | net10.0-android (.NET MAUI) | The problem: MAUI has no Linux target. |
| `App/Platforms/Android` | ~4k | Android | Must be replaced. |

The Views are mostly code-built (no XAML found): `SKCanvasView` wrappers calling Chrome painters, plus MAUI layouts (`Grid`, `Label`, stacks, `Border`).

Decision: keep the MAUI views and give MAUI a Linux backend, instead of rewriting the UI.

## 3. Backends considered
| Backend | Status | Notes |
|---|---|---|
| **OpenMaui** (`OpenMaui.Controls.Linux`) | Used in the spike | SkiaSharp-rendered; X11 and Wayland; versions track MAUI 10. |
| Avalonia MAUI backend | Preview | Reported as built on .NET 11 preview; reported to use X11/XWayland. |
| Maui.Gtk (GTK4) | Experimental | Likely too heavy for 1 GB RAM. |

## 4. What the spike did
1. Built `Domain`, `Chrome`, `Infrastructure` for `linux-arm64`: all succeeded.
2. Made a minimal OpenMaui app with an `SKCanvasView`: renders, `PaintSurface` fires, `PKForge.Chrome` links and runs.
3. Linked **PKForge's real `PickerMenu` view** and its sibling files (`Kit`, `DsKit`, `InfoKit`, `PadMenu`, `TextPopup`, `SummaryPlates`, `PixelFont`, theme and several services) **unchanged**. It compiled and ran.
4. Drove it with synthetic input (X11 XTest through python-xlib) and by calling `GamepadRouter` directly.

### Results
| Check | Result |
|---|---|
| Compile real views against OpenMaui | Pass. Every error was a missing sibling file; zero MAUI API incompatibilities. |
| Render (Skia painters, panels, badges) | Pass |
| App asset loading (`FileSystem.OpenAppPackageFileAsync("NDS12.ttf")`) | Pass, font loaded from the app package |
| Tap a row (first tap aims) | Pass |
| Drag to scroll | Pass |
| Tap search plate: MAUI `Entry` popup opens | Pass |
| Typed "sash": list filtered live to "Focus Sash" | Pass |
| Tap same row twice: picks | Pass (`PICKED Choice Scarf`) |
| Gamepad: `GamepadRouter.Dispatch` Down, Down, A | Pass (`PICKED Assault Vest`; start item was Life Orb, so two Downs land on it) |

### Two shims were needed (in `Shims/`)
- `VsyncLoop`: the Android version uses `Choreographer`. Replaced with a 16 ms `IDispatcherTimer`. A real port should tick from the display's refresh.
- `BoxBrowserPage`: `PickerMenu` only calls its static `PixelTypeface()`. The method was copied verbatim into a stub class.

## 5. Problems found
- **Package version conflict.** OpenMaui 10.0.130.1 requires `Microsoft.Maui.Controls` 10.0.130 and SkiaSharp 4.152.1. PKForge pins MAUI 10.0.20 and SkiaSharp 3.116.1. The restore fails (NU1605) until versions are aligned. `Chrome` was compiled against SkiaSharp 3 and ran against 4 in the spike, but that is not a full regression test. Upgrading PKForge itself to these versions is required.
- **Window size.** The window filled about 833×625 of a 1024×768 display. Full-screen sizing is not handled.
- **Log noise.** `SKCanvasView` logs "ERROR: Draw #n" on every draw even when drawing succeeds. Harmless so far, but unexplained.
- The spike's `MainPage` registers `GamepadRouter` as a singleton; the real app does this in `MauiProgram`.

## 6. Not verified (do these before trusting the port)
- Running on **ARM64 / the RG DS Plus** itself; **RAM use** against 1 GB (the app bundles many sprite and art assets).
- The device's display stack (X11, Wayland, or direct DRM/KMS) and whether OpenMaui can run on it.
- How the OS exposes the two panels (two outputs or one surface). Decides the second-screen design.
- `PKForge.Engine` and `AutoMod` building on Linux (needs the PKHeX submodules, `external/PKHeX`, which did not clone).
- Large views (`BankPage` 2.3k lines, `BoxBrowserPage` 6k lines, `MonSummaryView`), the Poképark, animations.
- Real gamepad input (evdev/SDL) and the SKCanvasView touch path on a real touchscreen.

## 7. How to reproduce the spike
```bash
# 1. .NET 10 SDK
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir ~/dotnet
export PATH=~/dotnet:$PATH

# 2. Layout: the spike now lives in this fork at ports/linux/SkiaSpike and builds against the repo root
git clone --recurse-submodules https://github.com/AlexWorland/PKForge && cd PKForge/ports/linux/SkiaSpike
# (pass -p:PKForge=/path/to/pkforge to build against a different checkout)

# 3. Build and run (X11 session or Xvfb)
dotnet build -r linux-x64 --no-self-contained
MAUI_PREFER_X11=1 xvfb-run -a -s "-screen 0 1024x768x24" dotnet bin/Debug/net10.0/linux-x64/SkiaSpike.dll
```
Use `-r linux-arm64` on the device. Use a real `pkforge` directory, not a symlink: a symlinked checkout broke the relative project references in my test. The archive was verified to build from a clean extract with `-p:PKForge=<path>`. The project file shows exactly which App files are linked and the package versions.

## 8. Plan to get the real port working
1. **Spike on the device.** Install .NET 10 arm64 and run `SkiaSpike`. Check the display backend, FPS, RAM, window size, touch. Gate for everything else.
2. **Align versions.** Move PKForge to MAUI 10.0.130 and SkiaSharp 4.x (or pick an OpenMaui build matching the old pins) and re-run the Android tests.
3. **Add a Linux app head** (a new project, `net10.0`, OpenMaui) sharing `Views`, `ViewModels`, `Services`, `Theme`, `Resources` with the Android head (shared project or linked files). Use a Linux `MauiProgram` with `.UseLinux()`.
4. **Split Android code.** Move the 44 `#if ANDROID` sites (about 12 files) behind interfaces: `VsyncLoop`, `AppLog`, `PerfTrace`, `AppUpdateService`, `WalkerSteps` first.
5. **Linux implementations of the existing contracts** in Domain: `ISaveFileAccess`, `IDocumentPicker`, `IFolderPicker`, `IFolderFileAccess` (plain `System.IO`, simpler than Android SAF), `IEmulatorDetectionService` (save paths for RetroArch and the RG DS Plus emulators; not yet checked).
6. **Gamepad.** Feed `GamepadRouter.Dispatch(PadButton)` and `DispatchStick` from evdev or SDL instead of Android key events. The router is already platform-neutral (tested above).
7. **Second screen.** Implement `ISecondaryDisplayHost` for Linux: either a second window on the other output or one wide surface split into two pages. `SecondScreenBoxPage` is already a standalone page, laid out for 1240×1080, so rescale for 1024×768.
8. **Remove or replace Android-only features:** home widgets, shortcuts, `MusicPlayer` (OpenMaui offers a GStreamer MediaElement).
9. **Build the Engine** with PKHeX submodules for `linux-arm64`; test load and save of real `.sav` files.
10. **Memory pass** on 1 GB: asset loading, sprite caches, `PublishReadyToRun`, trimming.

## 9. Risks
- OpenMaui and the Avalonia MAUI backend are young; expect bugs in less-used controls (`CollectionView`, Shapes, animations).
- RAM is the likeliest blocker.
- The upstream author has announced no Linux plans, so this is a fork or contribution.

## 10. Alternative
The RG DS Plus can reportedly run Android through GammaOS Nano, which would run the existing APK with no port. Not tested.
