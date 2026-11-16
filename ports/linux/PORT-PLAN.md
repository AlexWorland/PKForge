# PKForge Linux Port (RG DS Plus) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run the existing PKForge app on the Anbernic RG DS Plus (Linux only) by building a Linux container around the unchanged app code.

**Architecture:** A new project, `ports/linux/PKForge.Linux`, compiles the source files of `src/PKForge.App` by linking them, and references `Domain`, `Engine` and `Infrastructure` as they are. OpenMaui supplies the MAUI backend on Linux. Everything Android provided is supplied by the container under the names the shared code already expects: platform services, a few same-named replacement types, and a compile-only Android stub layer. No existing file under `src/` is edited.

**Tech Stack:** .NET 10 (`net10.0`, RID `linux-arm64`, self-contained), OpenMaui 10.0.130.1, Microsoft.Maui.Controls 10.0.130, SkiaSharp 4.152.1, SDL2 (gamepad only, via P/Invoke), xunit, a Wayland compositor on the device (sway or weston), PortMaster-style packaging.

**Spec:** `ports/linux/README.md` (spike results) plus the "Codebase analysis" section of this file. There is no separate spec document; the analysis below is the basis every task argues from.

## Global Constraints

- **Zero edits** to existing files under `src/`, `tests/`, `tools/`, `external/`, to `PKForge.sln`, `Directory.Build.props`, or `.github/workflows/build.yml`. New files live under `ports/linux/`, plus one new workflow `.github/workflows/linux-port.yml`.
- **Escape hatch:** if a seam truly cannot be built without touching `src/`, the only allowed edit is an additive `#else` branch on an existing or new `#if ANDROID` block that leaves the Android build identical. Each one is recorded in `ports/linux/INTRUSIONS.md` with file, lines and reason. One candidate is known today (Task 13).
- The Android build and both existing test projects must pass unchanged after every task: `dotnet test tests/PKForge.Domain.Tests/...`, `dotnet test tests/PKForge.Engine.Tests/...`.
- Target device: Anbernic RG DS Plus. Rockchip RK3568, **1 GB RAM**, two **1024×768** touch panels, Linux only, no internal storage (OS and games on microSD).
- Package pins in the Linux projects, exact versions: `OpenMaui.Controls.Linux` 10.0.130.1, `OpenMaui.Hosting` 10.0.130.1, `Microsoft.Maui.Controls` 10.0.130, `Microsoft.Maui.Graphics` 10.0.130, `SkiaSharp` 4.152.1, `SkiaSharp.Views.Maui.Controls` 4.152.1, `CommunityToolkit.Mvvm` 8.4.0, `ZXing.Net` 0.16.10. No floating versions.
- `ANDROID` and `DIAGNOSTIC` are never defined in the Linux projects.
- On Linux a `DocumentId` is an absolute file path and a `TreeId` is an absolute directory path.
- Every save write still goes through `ISafeSaveWriter`. The container only implements `ISaveFileAccess` underneath it.
- GPLv3. Attribution files (`Resources/UI/ATTRIBUTION.md`, `Resources/GameArt/ATTRIBUTION.md`, font licences) ship in the package.
- Files under `ports/linux/PKForge.Linux/Core/` must not reference MAUI or OpenMaui, so the test project can compile them on any OS.

## Review Focus

1. **Save written to an exFAT or FAT microSD card, power lost mid-write.** The user expects either the old save or the new one, never a truncated file. Pinned in Task 6 (`WriteLeavesNoTemporaryAndKeepsOldBytesOnFailure`).
2. **A data archive exported from the Android app, imported on Linux.** Its watched roots carry `content://` ids. The user expects no crash and the saves shown as needing a relink. Pinned in Task 6 (`ContentUriDocumentIdIsReportedMissingNotThrown`).
3. **Second panel missing, off, or the lid closed.** The user expects the app to stay usable on one screen and never hang on `ShowAsync`. Pinned in Task 11 (`HostIsUnavailableWithOneOutput`).
4. **No SDL2 library or no gamepad found.** The user expects the app to start and work by touch. Pinned in Task 9 (`PumpStartsWithoutSdlAndReportsUnavailable`).
5. **A text field focused on a device with no keyboard.** The user expects an on-screen keyboard, and cancelling it must leave the field's text as it was. Pinned in Task 10 (`CancelReturnsOriginalText`).

---

## Codebase analysis

Nothing in this section was compiled: the .NET SDK is not installed on the machine the analysis ran on. The inventory comes from reading and scripted scanning of the source. Task 3 turns it into a compiler-verified list.

### Size and portability by project

| Project | Files | Lines | Target | On Linux |
|---|---|---|---|---|
| `PKForge.Domain` | 121 | 21,104 | `net10.0` | Used as is |
| `PKForge.Engine` | 118 | 37,008 | `net10.0` | Used as is (needs both submodules) |
| `PKForge.Infrastructure` | 22 | 4,127 | `net10.0` | Used as is |
| `PKForge.Chrome` | 59 | 15,158 | `net10.0`, SkiaSharp 3.116.1 | Sources linked into a SkiaSharp 4 build (Task 2) |
| `PKForge.App` shared (`Views`, `Services`, `ViewModels`, `Theme`, `App.cs`, `MauiProgram.cs`) | 190 | 56,015 | `net10.0-android` | Sources linked into the Linux head |
| `PKForge.App/Platforms/Android` | 14 | 3,976 | Android | Not compiled. Replaced by the container. |

So 93% of the app-layer code is shared, and the Android platform folder is the part to replace.

### What the Android platform folder provides

| Android file | Lines | Provides | Linux container equivalent |
|---|---|---|---|
| `MainActivity.cs` | 795 | Key and stick input to `GamepadRouter` with hold-repeat (hat 320 ms then 80 ms; shoulders 380 ms then 140 ms), fullscreen, safe-area padding, widget and shortcut intents, UI scale, and `AndroidSecondaryDisplayHost` (the lower screen as an Android `Presentation`) | Tasks 9, 12; launcher script (Task 15) |
| `AndroidSafFileAccess.cs` | 50 | `ISaveFileAccess` over content URIs | Task 6 |
| `AndroidFolderFileAccess.cs` | 308 | `IFolderFileAccess`, `IFolderTreeAccess` | Task 6 |
| `AndroidDocumentPicker.cs`, `AndroidFolderPicker.cs` | 190 | `IDocumentPicker`, `IFolderPicker` (system pickers) | Task 8 (in-app file browser) |
| `AndroidEmulatorScanner.cs` | 619 | `IIncrementalEmulatorDetectionService`. Only 16 lines touch Android APIs; the rest is traversal and `EmulatorSaveHeuristics` calls. | Task 7 |
| `MusicPlayer.cs` | 296 | `IMusicPlayer` plus `PauseForBackground`, `ResumeFromBackground`, `MaybeAutostart`, `LastError` | Task 4 (silent stand-in), Task 14 (real audio, optional) |
| `HomeWidgets.cs`, `LiveParkWidget.cs`, `PokeparkWidget.cs`, `WidgetHost.cs`, `AppShortcuts.cs` | 1,678 | Home-screen widgets and launcher shortcuts | Task 4 (inert stand-ins; the feature does not exist on Linux) |

### Android references in the shared app code

A script tracked `#if` state per line and listed every Android type reference.

**Guarded (drop out on Linux, no work):** 20 `#if ANDROID` blocks in `MauiProgram.cs`, `App.cs`, `Kit.cs`, `CapsuleSkin.cs`, `EditorRows.cs`, `HomePage.cs`, `PokeparkPage.cs`, `AppLog.cs`, `PerfTrace.cs`, `WalkerSteps.cs`, `AppUpdateService.cs`. `PaintClock.cs` uses Android only under `DIAGNOSTIC`.

**Unguarded (would not compile on Linux):**

| File | Unguarded lines | What it uses | Plan |
|---|---|---|---|
| `Services/VsyncLoop.cs` | whole file | `Choreographer` | Replace file (Task 4) |
| `Services/AppUpdateService.cs` | line 2, and line 27 calls a guarded method | `using Android.App`, APK installer | Replace file (Task 4) |
| `Views/WidgetMenu.cs` | 14 | `HomeWidgets`, `LiveParkWidget`, `Android.App.Application` | Replace file (Task 4) |
| `Views/BadAppleEgg.cs` | 4 | `Android.Media.MediaPlayer` (About-screen easter egg) | Replace file (Task 4) |
| `Services/AppDataTransfer.cs` | 7 | `SharedPreferences`, `Java.Lang.*` value types | Compile against the stub layer (Task 3) |
| `Views/HomePage.cs` | 11 | `Android.Views.View` touch stream (lines 174–226), `Android.Util.Log` | Stub layer. Shelf-card touch is lost; see Task 13. |
| `Views/BoxBrowserPage.cs` | 5 | `HomeWidgets`, `HomeWidgetStore` | Same-named stand-ins (Task 4) |
| `Views/BankPage.cs` | 3 | `HomeWidgets` | Same-named stand-ins (Task 4) |
| `App.cs` | 2 | `Platforms.Android.MusicPlayer` | Same-named stand-in (Task 4) |
| `Views/MusicMenu.cs` | 1 | `Platforms.Android.MusicPlayer` | Same-named stand-in (Task 4) |
| `Views/SecondScreenBoxPage.cs` | 1 | `Java.Lang.Exception` in a catch filter | Stub layer (Task 3) |

`Views/WidgetLinks.cs` and `Views/PokeparkPage.cs` also use `HomeWidgetStore` and `PokeparkWidgetPublisher`, which are defined in the Android folder.

The spike README says "44 `#if ANDROID` sites (about 12 files)". The real figures are 20 guarded blocks in 11 files, plus the 11 files above with unguarded references.

### MAUI and Essentials surface the app uses

| Area | Use in shared code | Risk on OpenMaui |
|---|---|---|
| Layout and controls | `Grid` ×152, `Label` ×106, stacks ×76, `SKCanvasView` ×43, `TapGestureRecognizer` ×38, `Border` ×29, `RoundRectangle` ×24, `BoxView` ×20, `ScrollView` ×16, `Image` ×12, `Entry` ×8, `CollectionView` ×3, `FlexLayout` ×3, `Button` ×3, one `Editor`, one `ProgressBar`, one `NavigationPage`. No XAML, no Shell, no modal pages, no `DisplayAlert`. | Low. OpenMaui's compatibility scorecard lists all of these as covered. `RoundRectangle` and `LinearGradientBrush` (×4) are not listed: check in Task 5. |
| Skia touch | `EnableTouchEvents` in 40 files | Low. The spike exercised it. |
| `SKGLView` | Once, `Views/ParkCanvas.cs`, with a built-in CPU fallback chosen by the preference `pkforge.pokepark.renderer.v1` | Medium. If the GPU view misbehaves, set that preference to `cpu`. |
| Navigation | `Navigation.PushAsync`/`PopAsync` in 7 files, `OnBackButtonPressed` in 2 | Low |
| Essentials: storage | `Preferences` ×99 in 31 files, `FileSystem.AppDataDirectory` ×37, `CacheDirectory` ×31, `OpenAppPackageFileAsync` ×36 | Low. The spike loaded a package file. Data paths are redirected by the launcher (Task 15). |
| Essentials: threading and info | `MainThread` ×91, `DeviceDisplay` ×12, `AppInfo` ×13 | Medium. `MainThread` worked in the spike; `DeviceDisplay` and `AppInfo` are listed as covered. Verified in Task 5. |
| Essentials: desktop-style services | `Share` ×12, `FilePicker` ×4, `Clipboard` ×3, `Launcher` ×3, `HapticFeedback` ×2, `Connectivity` ×1 | High on a handheld. OpenMaui's versions assume a desktop (dialogs, a browser). The container replaces them (Task 8). |
| Network | `HttpClient` in 8 files: GitHub (updates, sprite pack), PMDCollab, PokeAPI | Low. Needs Wi-Fi and CA certificates on the device. |
| Text entry | 8 `Entry` sites and one `Editor` (`TextPopup`, `Kit`, `StatsPopup`, `PokedexPicker`, `BankSearchPage`, `HexEditorPage`, `EditorRows`) | High. The device has no keyboard and the firmware has no on-screen keyboard. Task 10. |

### Assets

`PKForge.App.csproj` declares 34 `MauiAsset` globs with explicit `LogicalName`s, about 77 MB on disk (Showdown sprites 12 MB, game logos 13 MB, PKHeX sprites and artwork 18 MB, fonts 10 MB, game art and hero banners 13 MB). They are read with `FileSystem.OpenAppPackageFileAsync("<logical name>")`. The Linux head has to declare the same set (Task 2). `SpriteService` caps its bitmap cache at 1,024 entries, so sprite memory is bounded.

### Device and firmware facts

- Specs: RK3568, 1 GB RAM, two 4.3" 1024×768 panels, Linux only. Confirmed by Anbernic's comparison page and several reviews.
- Firmwares that exist for it: Anbernic's stock Linux, ROCKNIX, KNULLI, BananaLinux, and GammaOS Nano. One article describes GammaOS Nano as Android-based.
- **BananaLinux drives both panels directly with no compositor.** OpenMaui needs X11 or Wayland, so on a firmware like that the launcher must start its own compositor. BananaLinux does support PortMaster, and PortMaster ships a "westonpack" runtime (weston plus Xwayland, 52.8 MB for aarch64).
- Not found by research, so they are recon items in Task 1: which display stack ROCKNIX, KNULLI and the stock firmware use on this device; how the two panels appear (two outputs or one); libc type and version; whether SDL2, fontconfig and EGL are present.
- OpenMaui documents X11 and Wayland backends, GPU rendering through EGL with a CPU fallback (`OPENMAUI_RENDERER=gpu|raster|auto`), and a diagnostics switch (`OPENMAUI_DOCTOR=1`). Its documentation says nothing about multiple windows, gamepads, or fullscreen control. Multi-window support decides how the second screen is built (Task 1, Task 12).
- Upstream already supports the original RG DS on Android through GammaOS: `MainActivity.cs` and `AndroidSecondaryDisplayHost` contain RG DS handling, so the app's layouts already cope with 4:3 panels.

### How the container hooks in without editing `src/`

1. **Linked sources.** The head compiles `src/PKForge.App/**/*.cs` except `Platforms/**` and four replaced files.
2. **Same-named replacements.** The head defines `VsyncLoop`, `AppUpdateService`, `WidgetMenu`, `BadAppleEgg`, `HomeWidgets`, `HomeWidgetStore`, `PokeparkWidgetPublisher` and `PKForge.App.Platforms.Android.MusicPlayer` with the members the shared code calls.
3. **Android stub layer.** A small set of empty `Android.*` and `Java.Lang.*` types lets stray unguarded lines compile. At run time those code paths never match (`view.Handler?.PlatformView is Android.Views.View` is false), so they do nothing.
4. **Hosting seam.** `MauiProgram.cs` calls `builder.UseMauiApp<App>().UseSkiaSharp()`. C# resolves extension methods in the enclosing namespace before `using` imports, so an extension `UseSkiaSharp(this MauiAppBuilder)` declared in namespace `PKForge.App` by the head is the one that call binds to. It calls the real SkiaSharp method, then `UseLinux()`, then registers the Linux services. The shared composition root stays the single list of services.
5. **Essentials overrides.** MAUI's `Share`, `FilePicker`, `Clipboard`, `Launcher` and `HapticFeedback` each have a non-public static `SetDefault`. The container calls it by reflection to install handheld-friendly versions.

---

## Prerequisites

- .NET 10 SDK. The head compiles and the unit tests run on macOS, Linux or Windows. No MAUI workload is needed for the Linux projects.
- A Linux x64 machine, container or CI runner to *run* the app (Xvfb, and sway for Task 12). The app does not run on macOS.
- The device, with shell access, for Tasks 1, 9, 12 and 15.
- Both submodules checked out (`git submodule update --init --recursive`).

## File structure

```
ports/linux/
  README.md                         spike results (exists)
  PORT-PLAN.md                      this plan
  DECISIONS.md                      recon results and the choices they settle (Task 1)
  INTRUSIONS.md                     any edit made under src/ (empty unless Task 13 is taken)
  recon/recon.sh                    device fact collector (Task 1)
  SkiaSpike/                        spike (exists; gains a second-window probe in Task 1)
  PKForge.Chrome.Skia4/
    PKForge.Chrome.Skia4.csproj     links src/PKForge.Chrome sources, SkiaSharp 4.152.1
  PKForge.Linux/
    PKForge.Linux.csproj            the head: Exe, net10.0, linked App sources
    Assets.props                    the MauiAsset list, mirrored from PKForge.App.csproj
    Program.cs                      Main
    Hosting/LinuxHosting.cs         UseSkiaSharp seam, service registration
    Hosting/LinuxEssentials.cs      Share/FilePicker/Clipboard/Launcher/Haptic overrides
    AndroidShim/AndroidShim.cs      compile-only Android.* and Java.Lang.* types
    Seams/                          same-named replacements
      VsyncLoop.cs  AppUpdateService.cs  WidgetMenu.cs  BadAppleEgg.cs
      HomeWidgets.cs  MusicPlayer.cs
    Core/                           no MAUI references; unit tested
      LinuxPaths.cs  LinuxSaveFileAccess.cs  LinuxFolderFileAccess.cs
      LinuxEmulatorScanner.cs  PadMapping.cs  PadRepeater.cs  SdlGamepadPump.cs
      FileBrowserModel.cs  OskModel.cs  ExportFolder.cs  OutputLayout.cs
    Platform/                       MAUI-facing container code
      FileBrowserPage.cs  LinuxPickers.cs
      OskPage.cs  OskHook.cs  LinuxSecondaryDisplayHost.cs
  PKForge.Linux.Tests/
    PKForge.Linux.Tests.csproj      xunit; compiles ../PKForge.Linux/Core/**/*.cs
  packaging/
    PKForge.sh                      launcher (compositor, env, data paths)
    sway.conf  weston.ini           window placement per compositor
    build.sh                        publish linux-arm64 and assemble the port folder
    port.json                       PortMaster metadata
.github/workflows/linux-port.yml    build, test and smoke the Linux head
```

---

## Phase 0: Gate

### Task 1: Device recon and decisions

Everything after this task depends on facts only the device can give. Do not start Task 2 until `DECISIONS.md` is filled in.

**Files:**
- Create: `ports/linux/recon/recon.sh`
- Create: `ports/linux/DECISIONS.md`
- Modify: `ports/linux/SkiaSpike/SkiaSpike.csproj` (pin OpenMaui to 10.0.130.1)
- Modify: `ports/linux/SkiaSpike/MainPage.cs` (second-window probe)

**Interfaces:**
- Produces: `DECISIONS.md` with one line per key below. Later tasks read these keys by name.
  - `FIRMWARE` (primary target), `LIBC` (`glibc <version>` or `musl`), `COMPOSITOR` (`sway`, `weston`, `x11`, or `none`), `OUTPUTS` (names and modes of both panels, and which is physically on top), `MULTI_WINDOW` (`yes` or `no`), `SDL2` (`yes` or `no`), `PAD_LAYOUT` (`nintendo` or `xbox`), `RENDERER` (`gpu` or `raster`), `SAVE_ROOTS` (directories where the firmware's emulators keep saves), `IDLE_RSS_MB` (spike memory on the device).

- [ ] **Step 1: Write `recon.sh`.** It prints, each under a heading: `uname -a`; `/etc/os-release`; `ldd --version` first line; `free -m`; for every `/sys/class/drm/card*-*` its `status` and `modes`; `$WAYLAND_DISPLAY`, `$DISPLAY`, `$XDG_RUNTIME_DIR`; which of `sway weston cage Xorg swaymsg wlr-randr` exist; `ls` matches for `libSDL2-2.0.so.0`, `libfontconfig.so.1`, `libEGL.so.1`, `libGLESv2.so.2`, `libgstreamer-1.0.so.0`; `/proc/bus/input/devices`; whether `/roms/ports/PortMaster` or `/storage/roms/ports/PortMaster` exists; and `find / -xdev \( -name '*.srm' -o -name '*.dsv' -o -name '*.sav' \) 2>/dev/null | head -50`.
- [ ] **Step 2: Run it on the device on each candidate firmware** (ROCKNIX first, then the stock firmware) and save the output as `ports/linux/recon/<firmware>.txt`.
- [ ] **Step 3: Pin the spike's OpenMaui packages** to `10.0.130.1` in `SkiaSpike.csproj` (both `OpenMaui.Hosting` and `OpenMaui.Controls.Linux`).
- [ ] **Step 4: Add the second-window probe.** When the environment variable `SPIKE_SECOND_WINDOW=1` is set, `MainPage` calls `Application.Current!.OpenWindow(new Window(new ContentPage { Title = "PKForge Lower", Content = new Label { Text = "LOWER" } }) { Title = "PKForge Lower" })` after load and prints `SECOND WINDOW opened` or the exception.
- [ ] **Step 5: Run the spike on the device.**

Run: `dotnet publish -r linux-arm64 --self-contained -c Release` on a Linux machine, copy the publish folder to the device, then `OPENMAUI_DOCTOR=1 SPIKE_SECOND_WINDOW=1 ./SkiaSpike`.
Expected: the picker draws, `PICKED Assault Vest` is printed, and either `SECOND WINDOW opened` or an exception that settles `MULTI_WINDOW`. Record resident memory from `ps -o rss= -C SkiaSpike`.
- [ ] **Step 6: Fill in `DECISIONS.md`** with every key above and these rules applied:
  - `COMPOSITOR=none` on the chosen firmware means the launcher bundles weston (Task 15 uses `weston.ini`); otherwise it uses the running compositor.
  - `MULTI_WINDOW=no` means Task 12 ships the single-screen fallback and the second screen is deferred.
  - `LIBC=musl` means every publish command in this plan uses `linux-musl-arm64`.
  - `IDLE_RSS_MB` above 300 stops the plan: report to the user before going on, because the full app will not fit in 1 GB.
- [ ] **Step 7: Commit**

```bash
git add ports/linux/recon ports/linux/DECISIONS.md ports/linux/SkiaSpike
git commit -m "docs: RG DS Plus recon results and port decisions"
```

---

## Phase 1: The head compiles and starts

### Task 2: Project skeletons, Chrome on SkiaSharp 4, asset list

**Files:**
- Create: `ports/linux/PKForge.Chrome.Skia4/PKForge.Chrome.Skia4.csproj`
- Create: `ports/linux/PKForge.Linux/PKForge.Linux.csproj`
- Create: `ports/linux/PKForge.Linux/Assets.props`
- Create: `ports/linux/PKForge.Linux/Program.cs`
- Create: `ports/linux/PKForge.Linux.Tests/PKForge.Linux.Tests.csproj`
- Test: `ports/linux/PKForge.Linux.Tests/AssetParityTests.cs`

**Interfaces:**
- Produces: assembly `PKForge.Chrome` (same name as the original, built against SkiaSharp 4.152.1); the head project with MSBuild property `$(PKForgeRoot)` pointing at the repository root; `Assets.props` imported by the head.

- [ ] **Step 1: Write the failing tests in `AssetParityTests`.**

`LinuxAssetsMirrorTheAndroidProject` parses `src/PKForge.App/PKForge.App.csproj` and `ports/linux/PKForge.Linux/Assets.props` as XML, collects every `MauiAsset` and `MauiFont` item as the tuple (`Include` with the leading path normalised to repo-relative, `Exclude`, `LogicalName`), leaves out any `ItemGroup` in `Assets.props` labelled `LinuxOnly`, and asserts the two sets are equal. It finds the repository root by walking up from `AppContext.BaseDirectory` to the folder that holds `PKForge.sln`.

`VersionMatchesTheAndroidProject` asserts that `ApplicationVersion` and `ApplicationDisplayVersion` in `PKForge.Linux.csproj` equal those in `PKForge.App.csproj`.

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test ports/linux/PKForge.Linux.Tests --filter "FullyQualifiedName~AssetParityTests"`
Expected: FAIL, `Assets.props` not found.

- [ ] **Step 3: Create `PKForge.Chrome.Skia4.csproj`.** `net10.0`, `AssemblyName` and `RootNamespace` `PKForge.Chrome`, `Compile Include="../../../src/PKForge.Chrome/**/*.cs"` excluding `bin/**` and `obj/**`, `PackageReference SkiaSharp 4.152.1`, `ProjectReference` to `src/PKForge.Domain`.
- [ ] **Step 4: Build it**

Run: `dotnet build ports/linux/PKForge.Chrome.Skia4`
Expected: build succeeds. Any error is a SkiaSharp 3 to 4 API break in a painter; list each in `DECISIONS.md` under `SKIA4_BREAKS`. If there are any, stop and report: fixing them needs either an edit under `src/PKForge.Chrome` or a different OpenMaui version, and that is the user's call.

- [ ] **Step 5: Create `Assets.props`** holding every `MauiAsset` and `MauiFont` item from `PKForge.App.csproj`, with paths rewritten to start at `$(PKForgeRoot)`. Add one `ItemGroup Label="LinuxOnly"` that also ships the two fonts the code reads as package files, as the spike had to: `MauiAsset` for `Resources/Fonts/NDS12.ttf` with `LogicalName="NDS12.ttf"` and for `Resources/Fonts/MPLUSRounded1c-Regular.ttf` with `LogicalName="MPLUSRounded1c-Regular.ttf"`.
- [ ] **Step 6: Create `PKForge.Linux.csproj`.** `OutputType Exe`, `net10.0`, `RootNamespace PKForge.App`, `AssemblyName PKForge`, `Nullable enable`, `ImplicitUsings enable`, `TreatWarningsAsErrors false`, `ApplicationTitle PKForge`, `ApplicationVersion 320`, `ApplicationDisplayVersion 3.2.0`, `InvariantGlobalization true`, `PublishReadyToRun true`. The pinned packages from Global Constraints. `Compile Include="$(PKForgeRoot)/src/PKForge.App/**/*.cs"` with `Exclude` covering `Platforms/**`, `bin/**`, `obj/**`, `Services/VsyncLoop.cs`, `Services/AppUpdateService.cs`, `Views/WidgetMenu.cs`, `Views/BadAppleEgg.cs`, and `Link="App/%(RecursiveDir)%(Filename)%(Extension)"`. Project references to `Domain`, `Engine`, `Infrastructure` and `PKForge.Chrome.Skia4`. `Import Project="Assets.props"`.
- [ ] **Step 7: Create `Program.cs`**: `Main` calls `PKForge.App.MauiProgram.CreateMauiApp()` and passes the result to `Microsoft.Maui.Platform.Linux.LinuxApplication.Run(app, args)`.
- [ ] **Step 8: Create the test project.** xunit packages at the versions the existing test projects use (`Microsoft.NET.Test.Sdk` 17.14.1, `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.4), `Compile Include="../PKForge.Linux/Core/**/*.cs"`, a linked compile of `src/PKForge.App/Services/GamepadRouting.cs`, project references to `Domain`, `Infrastructure`, `Engine`.
- [ ] **Step 9: Run the test to verify it passes**

Run: `dotnet test ports/linux/PKForge.Linux.Tests --filter "FullyQualifiedName~AssetParityTests"`
Expected: PASS, 2 tests.

- [ ] **Step 10: Commit**

```bash
git add ports/linux/PKForge.Chrome.Skia4 ports/linux/PKForge.Linux ports/linux/PKForge.Linux.Tests
git commit -m "feat(linux): head, Chrome-on-Skia4 and test project skeletons"
```

### Task 3: Hosting seam and Android stub layer

**Files:**
- Create: `ports/linux/PKForge.Linux/Hosting/LinuxHosting.cs`
- Create: `ports/linux/PKForge.Linux/AndroidShim/AndroidShim.cs`

**Interfaces:**
- Produces:
  - `namespace PKForge.App; public static class LinuxHosting { public static MauiAppBuilder UseSkiaSharp(this MauiAppBuilder builder); internal static void RegisterPlatformServices(IServiceCollection services); }`. `UseSkiaSharp` calls `SkiaSharp.Views.Maui.Controls.Hosting.AppHostBuilderExtensions.UseSkiaSharp(builder)`, then `builder.UseLinux()`, then `RegisterPlatformServices(builder.Services)`, and returns the builder. `RegisterPlatformServices` is empty in this task; Tasks 6 to 12 add lines to it.
  - Stub types, all in `AndroidShim.cs`, none with behaviour: `Java.Lang.Exception : System.Exception`; `Java.Lang.String`, `Boolean`, `Integer`, `Long`, `Float` with the value methods `AppDataTransfer.cs` calls (`BooleanValue()`, `IntValue()`, `LongValue()`, `FloatValue()`); `Android.Util.Log` with static `Warn`, `Error`, `Info` that forward to `PKForge.App.Services.AppLog`; `Android.Views.View`, `ViewConfiguration`, `MotionEvent`, `MotionEventActions`, `FeedbackConstants` with exactly the members `HomePage.cs` lines 174–226 use; `Android.App.Application.Context`; `Android.Content.Context`, `ISharedPreferences` (`All` returns an empty dictionary), `FileCreationMode`.

- [ ] **Step 1: Write `LinuxHosting.cs`** with a header comment that explains the name-resolution rule it relies on and names `MauiProgram.cs` as the caller.
- [ ] **Step 2: Write `AndroidShim.cs`** with the types above.
- [ ] **Step 3: Build and record the compile inventory**

Run: `dotnet build ports/linux/PKForge.Linux 2>&1 | tee ports/linux/recon/compile-inventory.txt`
Expected at this point: errors only for the types Task 4 supplies (`VsyncLoop`, `AppUpdateService`, `WidgetMenu`, `BadAppleEgg`, `HomeWidgets`, `HomeWidgetStore`, `PokeparkWidgetPublisher`, `Platforms.Android.MusicPlayer`). Any other error is something static analysis missed: add the missing stub member to `AndroidShim.cs` if it is an Android type, or add it to Task 4's list if it is a PKForge type.

- [ ] **Step 4: Commit**

```bash
git add ports/linux/PKForge.Linux ports/linux/recon/compile-inventory.txt
git commit -m "feat(linux): hosting seam and compile-only Android stubs"
```

### Task 4: Same-named replacements until the head builds

**Files:**
- Create: `ports/linux/PKForge.Linux/Seams/VsyncLoop.cs`, `AppUpdateService.cs`, `WidgetMenu.cs`, `BadAppleEgg.cs`, `HomeWidgets.cs`, `MusicPlayer.cs`

**Interfaces:**
- Consumes: the compile inventory from Task 3. The member lists below come from static analysis; the compiler's list wins where they differ.
- Produces (each in the namespace of the type it stands in for):
  - `PKForge.App.Services.VsyncLoop`: `VsyncLoop(Func<float, bool> step)`, `bool IsRunning`, `void Start()`, `void Stop()`. Steps from an `IDispatcherTimer` at 16 ms, passes the real elapsed milliseconds capped at 50, and on an exception from `step` stops and calls `AppLog.Error("frame", "An animation step failed; it was stopped", error)` as the original does.
  - `PKForge.App.Services.AppUpdateService`: the original's public members except the APK installer. `CheckAsync` returns the "up to date" result without a network call. `CanInstallUpdates()` returns `false`. `OpenInstallPermissionSettings()` does nothing. `DownloadAndInstallAsync` returns `AppUpdateInstallResult.ReleasePageOpened` without downloading.
  - `PKForge.App.Views.WidgetMenu`: `ShowAsync` and `OfferOnceAsync` with the original signatures, both returning completed tasks.
  - `PKForge.App.Views.BadAppleEgg`: the constructor, `PlayAsync` and `Paint` with the original signatures; `PlayAsync` completes at once and `Paint` draws nothing.
  - `PKForge.App.HomeWidgets`: `PartnerFrom(...)` returns `null`; `SetPartnerAsync`, `CaptureOpenSaveAsync` return completed tasks; `HasAny(HomeWidgetKind)` returns `false`.
  - `PKForge.App.HomeWidgetStore`: `Settings` returns a default instance, `Last` returns `null`, `Eggs` returns empty, `Save` does nothing. `PKForge.App.LastSave` record as in the original.
  - `PKForge.App.PokeparkWidgetPublisher`: `HasActive`, `HasActiveWidgets` return `false`; `Read` returns `null`; `RefreshAsync` returns a completed task.
  - `PKForge.App.Platforms.Android.MusicPlayer : IMusicPlayer, IDisposable`: an empty library, `IsOn` and `IsPlaying` false, every command a no-op, plus `PauseForBackground()`, `ResumeFromBackground()`, `MaybeAutostart()` and `string? LastError => null`.

- [ ] **Step 1: Write the six files**, reading each original for exact signatures.
- [ ] **Step 2: Register the music stand-in.** In `LinuxHosting.RegisterPlatformServices` add `services.AddSingleton<Platforms.Android.MusicPlayer>()` and `services.AddSingleton<IMusicPlayer>(sp => sp.GetRequiredService<Platforms.Android.MusicPlayer>())`, matching the Android registration.
- [ ] **Step 3: Build**

Run: `dotnet build ports/linux/PKForge.Linux`
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 4: Confirm the Android side is untouched**

Run: `git status --porcelain src tests tools external PKForge.sln Directory.Build.props`
Expected: no output.

- [ ] **Step 5: Commit**

```bash
git add ports/linux/PKForge.Linux
git commit -m "feat(linux): same-named stand-ins for Android-only types; head builds"
```

### Task 5: The app starts and shows Home under a virtual display

**Files:**
- Create: `ports/linux/packaging/smoke.sh`
- Modify: `ports/linux/PKForge.Linux/Hosting/LinuxHosting.cs`

**Interfaces:**
- Produces: `smoke.sh [seconds]`, exit code 0 when the app survives and logs no errors. The environment variable `PKFORGE_PAD_SCRIPT` (a comma-separated list of `PadButton` names) is read by the head 3 seconds after start and each button is sent through `GamepadRouter.Dispatch` 500 ms apart; it is the test hook later tasks use.

- [ ] **Step 1: Add temporary file-access registrations** so the DI graph resolves: in `RegisterPlatformServices`, register throwing placeholders for `ISaveFileAccess`, `IDocumentPicker`, `IFolderPicker`, `IFolderFileAccess`, `IEmulatorDetectionService` and an `ISecondaryDisplayHost` whose `IsAvailable` is `false`. Tasks 6 to 12 replace them one by one.
- [ ] **Step 2: Add the `PKFORGE_PAD_SCRIPT` hook** in `LinuxHosting` (a background task started from `UseSkiaSharp`, dispatching on the main thread).
- [ ] **Step 3: Write `smoke.sh`.** It sets `XDG_DATA_HOME`, `XDG_CACHE_HOME`, `XDG_CONFIG_HOME` to a fresh temp folder, starts the built app under `xvfb-run -a -s "-screen 0 1024x768x24"` with `MAUI_PREFER_X11=1`, waits the given seconds (default 25), takes a screenshot with `xwd -root | convert xwd:- smoke.png`, then fails if the process has exited, if any `pkforge.log` under the temp data folder contains ` ERROR `, or if a `crash-*.txt` exists there.
- [ ] **Step 4: Run it on a Linux x64 machine**

Run: `dotnet build ports/linux/PKForge.Linux -r linux-x64 && ports/linux/packaging/smoke.sh`
Expected: exit 0, and `smoke.png` shows the Home screen with its first-run prompt.

- [ ] **Step 5: Check the Essentials the app depends on.** With the app running, read `pkforge.log`: no `NotImplementedInReferenceAssemblyException`. If one appears for `DeviceDisplay`, `AppInfo` or `Preferences`, record it in `DECISIONS.md` under `ESSENTIALS_GAPS` and add an override for that service to Task 8's list.
- [ ] **Step 6: Check `RoundRectangle` borders and gradient brushes** by eye in the screenshot (Home uses both). Record the result in `DECISIONS.md` under `RENDER_GAPS`.
- [ ] **Step 7: Commit**

```bash
git add ports/linux/packaging/smoke.sh ports/linux/PKForge.Linux ports/linux/DECISIONS.md
git commit -m "feat(linux): app boots to Home under Xvfb; smoke script"
```

---

## Phase 2: Files and saves

### Task 6: Path-based save and folder access

**Files:**
- Create: `ports/linux/PKForge.Linux/Core/LinuxSaveFileAccess.cs`, `Core/LinuxFolderFileAccess.cs`, `Core/LinuxPaths.cs`
- Modify: `ports/linux/PKForge.Linux/Hosting/LinuxHosting.cs`
- Test: `ports/linux/PKForge.Linux.Tests/LinuxSaveFileAccessTests.cs`, `LinuxFolderFileAccessTests.cs`, `LinuxPathsTests.cs`

**Interfaces:**
- Produces:
  - `namespace PKForge.Linux.Core; public sealed class LinuxSaveFileAccess : ISaveFileAccess`. `ReadAsync` reads the file at `documentId`; a `documentId` that is not a rooted path, or a missing file, throws `FileNotFoundException`. `WriteAtomicallyAsync` delegates to `PKForge.Infrastructure.AtomicFile.WriteAsync(path, bytes, cancellationToken)`.
  - `public sealed class LinuxFolderFileAccess : IFolderFileAccess, IFolderTreeAccess`. `ListFilesAsync(treeId)` returns the files directly inside the directory as `PickedDocument(fullPath, fileName)`. `IFolderTreeAccess` paths are relative to `treeId` with `/` separators; any relative path that resolves outside `treeId` throws `UnauthorizedAccessException`.
  - `public static class LinuxPaths { public static IReadOnlyList<string> SuggestedRoots(Func<string, bool> directoryExists, string home, IEnumerable<string> extra); }`. Returns, in this order and only those that exist: each entry of `extra` (the `SAVE_ROOTS` from `DECISIONS.md`, passed through the environment variable `PKFORGE_SAVE_ROOTS`, colon-separated), `/storage/roms`, `/roms`, `/roms2`, `/userdata/roms`, `/mnt/sdcard`, `/media`, then `home`. No duplicates.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task WriteThenReadReturnsTheSameBytes()            // 64 KiB of random bytes round-trip
[Fact] public async Task WriteLeavesNoTemporaryAndKeepsOldBytesOnFailure()
    // write A; make the directory read-only; writing B throws; file still equals A;
    // restore permissions; directory holds exactly one file
[Fact] public async Task ContentUriDocumentIdIsReportedMissingNotThrown()
    // ReadAsync("content://com.android.externalstorage.documents/x") throws FileNotFoundException
[Fact] public async Task ListFilesReturnsOnlyDirectChildrenWithFullPathIds()
[Fact] public async Task TreeWriteOutsideTheRootIsRefused()            // path "../escape.bin" throws UnauthorizedAccessException
[Fact] public void SuggestedRootsKeepsOrderAndDropsMissingAndDuplicates()
    // exists: /roms, /home/u ; extra: ["/roms"] ; result: ["/roms", "/home/u"]
```

The two tests that change permissions (here and in Task 7) return early when the process runs as root, because root ignores them.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test ports/linux/PKForge.Linux.Tests --filter "FullyQualifiedName~LinuxSaveFileAccessTests|FullyQualifiedName~LinuxFolderFileAccessTests|FullyQualifiedName~LinuxPathsTests"`
Expected: FAIL, types not defined.

- [ ] **Step 3: Implement the three classes.**
- [ ] **Step 4: Run the tests to verify they pass**

Run: the command from Step 2.
Expected: PASS, 6 tests.

- [ ] **Step 5: Register them**, replacing the placeholders: `ISaveFileAccess` to `LinuxSaveFileAccess`, `IFolderFileAccess` to `LinuxFolderFileAccess` (both singletons).
- [ ] **Step 6: Commit**

```bash
git add ports/linux/PKForge.Linux ports/linux/PKForge.Linux.Tests
git commit -m "feat(linux): path-based save and folder access"
```

### Task 7: Emulator save scanner

**Files:**
- Create: `ports/linux/PKForge.Linux/Core/LinuxEmulatorScanner.cs`
- Modify: `ports/linux/PKForge.Linux/Hosting/LinuxHosting.cs`
- Test: `ports/linux/PKForge.Linux.Tests/LinuxEmulatorScannerTests.cs`

**Interfaces:**
- Consumes: `PKForge.Infrastructure.EmulatorSaveHeuristics` (`IsCandidateFileName(name, kind)`, `IsDolphinMemoryCard`, `IsEdenSaveFileName`, `GuessSwitchGameLabel`, `FindSiblingRom`, `FolderHint`, `RequiresExtraCare`, `Normalize`), `ISaveEngine`.
- Produces: `public sealed class LinuxEmulatorScanner(ISaveEngine engine) : IIncrementalEmulatorDetectionService`. It follows the pipeline of `src/PKForge.App/Platforms/Android/AndroidEmulatorScanner.cs` step for step, with `Directory.EnumerateFiles(treeId, "*", SearchOption.AllDirectories)` in place of the content-resolver traversal. Unreadable subdirectories are skipped and named in `Diagnostics`. Symbolic links to directories are not followed.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task FindsAValidSaveAndRejectsALookalike()
    // tree holds a real blank Gen 3 save from the engine named "emerald.srm"
    // and a 32 KiB zero file named "notasave.srm":
    // result.Saves has exactly one entry whose DocumentId is the full path of emerald.srm;
    // result.RejectedCandidates contains "notasave.srm"; result.FilesSeen == 2
[Fact] public async Task IncrementalScanEndsWithACompleteUpdate()     // the last update has IsComplete == true
[Fact] public async Task UnreadableSubfolderIsSkippedAndReported()     // chmod 000 a subfolder; scan completes; Diagnostics names it
```

The blank save is produced the way `tests/PKForge.Engine.Tests/BlankSessionTests.cs` produces one.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test ports/linux/PKForge.Linux.Tests --filter "FullyQualifiedName~LinuxEmulatorScannerTests"`
Expected: FAIL, type not defined.

- [ ] **Step 3: Implement `LinuxEmulatorScanner`.**
- [ ] **Step 4: Run the tests to verify they pass**

Run: the command from Step 2.
Expected: PASS, 3 tests.

- [ ] **Step 5: Register it** as `IEmulatorDetectionService` (singleton), replacing the placeholder.
- [ ] **Step 6: Commit**

```bash
git add ports/linux/PKForge.Linux ports/linux/PKForge.Linux.Tests
git commit -m "feat(linux): emulator save scanner over System.IO"
```

### Task 8: In-app file browser, pickers, and handheld Essentials

**Files:**
- Create: `ports/linux/PKForge.Linux/Core/FileBrowserModel.cs`, `Core/ExportFolder.cs`
- Create: `ports/linux/PKForge.Linux/Platform/FileBrowserPage.cs`, `Platform/LinuxPickers.cs`
- Create: `ports/linux/PKForge.Linux/Hosting/LinuxEssentials.cs`
- Modify: `ports/linux/PKForge.Linux/Hosting/LinuxHosting.cs`
- Test: `ports/linux/PKForge.Linux.Tests/FileBrowserModelTests.cs`, `ExportFolderTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class FileBrowserModel(Func<string, IEnumerable<(string Name, bool IsDirectory)>> list, IReadOnlyList<string> roots)`. Members: `string? Current` (null means the list of roots), `IReadOnlyList<BrowserRow> Rows` (directories first, then files, each group sorted ordinal-ignore-case; hidden names starting with `.` left out; a leading `..` row unless at the roots list), `void Enter(int row)`, `void Up()`, `record BrowserRow(string Name, string FullPath, bool IsDirectory)`.
  - `public static class ExportFolder { public static string Copy(string sourcePath, string exportRoot, DateTimeOffset now); }`. Copies the file into `exportRoot`, creating it, and returns the new path. If the name is taken it appends `-yyyyMMdd-HHmmss` before the extension.
  - `FileBrowserPage : ContentPage, IPadHandler`, driven by pad and touch, built with the app's own `Kit`/`PickerMenu` styling. `static Task<string?> PickFileAsync(INavigation nav, string title)`, `static Task<IReadOnlyList<string>> PickFilesAsync(...)`, `static Task<string?> PickFolderAsync(...)`. Start pressed on a directory picks that directory in folder mode; B goes up, and B at the roots list cancels.
  - `LinuxDocumentPicker : IDocumentPicker`, `LinuxFolderPicker : IFolderPicker`, both in `LinuxPickers.cs`, both opening `FileBrowserPage` on the current navigation stack.
  - `LinuxEssentials.Install()`: sets, through the non-public static `SetDefault` on each MAUI class, an `IShare` that calls `ExportFolder.Copy` into `<data>/exports` and shows the app's status toast with the path; an `IFilePicker` backed by `FileBrowserPage`; an in-memory `IClipboard`; an `ILauncher` whose `OpenAsync` returns `false`; an `IHapticFeedback` that does nothing. It throws `InvalidOperationException` naming the class if a `SetDefault` method is not found.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void RowsListDirectoriesFirstThenFilesAndHideDotNames()
[Fact] public void EnterThenUpReturnsToTheParent()
[Fact] public void UpAtARootShowsTheRootsListWithNoParentRow()
[Fact] public void CopyKeepsTheNameWhenFree()
[Fact] public void CopyAddsATimestampWhenTheNameIsTaken()
    // now = 2026-11-15T10:20:30Z, existing "team.txt" -> "team-20261115-102030.txt"
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test ports/linux/PKForge.Linux.Tests --filter "FullyQualifiedName~FileBrowserModelTests|FullyQualifiedName~ExportFolderTests"`
Expected: FAIL, types not defined.

- [ ] **Step 3: Implement `FileBrowserModel` and `ExportFolder`.**
- [ ] **Step 4: Run the tests to verify they pass**

Run: the command from Step 2.
Expected: PASS, 5 tests.

- [ ] **Step 5: Implement `FileBrowserPage`, the two pickers and `LinuxEssentials`**; call `LinuxEssentials.Install()` from `LinuxHosting.UseSkiaSharp` after `UseLinux()`, and register the pickers in place of the placeholders.
- [ ] **Step 6: Verify end to end under Xvfb**

Run: `PKFORGE_SAVE_ROOTS=/tmp/pkf-saves PKFORGE_PAD_SCRIPT="A,A,A" ports/linux/packaging/smoke.sh 40` with a blank Gen 3 save placed at `/tmp/pkf-saves/emerald.srm` beforehand.
Expected: exit 0; the screenshot shows the save on the Home shelf; `pkforge.log` has no ` ERROR `. Adjust the pad script to the actual first-run steps and record the working script in `smoke.sh` as a comment.

- [ ] **Step 7: Commit**

```bash
git add ports/linux/PKForge.Linux ports/linux/PKForge.Linux.Tests ports/linux/packaging/smoke.sh
git commit -m "feat(linux): file browser, pickers and handheld Essentials"
```

---

## Phase 3: Input

### Task 9: Gamepad through SDL2

**Files:**
- Create: `ports/linux/PKForge.Linux/Core/PadMapping.cs`, `Core/PadRepeater.cs`, `Core/SdlGamepadPump.cs`
- Modify: `ports/linux/PKForge.Linux/Hosting/LinuxHosting.cs`
- Test: `ports/linux/PKForge.Linux.Tests/PadMappingTests.cs`, `PadRepeaterTests.cs`, `SdlGamepadPumpTests.cs`

**Interfaces:**
- Consumes: `PKForge.App.Services.PadButton`, `GamepadRouter.Dispatch`, `DispatchRelease`, `DispatchStick`, `TopPagesWithShoulders`; `PAD_LAYOUT` from `DECISIONS.md`.
- Produces:
  - `public enum SdlButton { A = 0, B = 1, X = 2, Y = 3, Back = 4, Guide = 5, Start = 6, LeftStick = 7, RightStick = 8, LeftShoulder = 9, RightShoulder = 10, DpadUp = 11, DpadDown = 12, DpadLeft = 13, DpadRight = 14 }` (the values of `SDL_GameControllerButton`).
  - `public enum PadLayout { Nintendo, Xbox }`
  - `public static class PadMapping { public static PadButton? Resolve(SdlButton button, PadLayout layout); public static PadLayout LayoutFrom(string? environmentValue); }`. `Nintendo` swaps A with B and X with Y, so the button printed "A" on the device confirms. `LayoutFrom` reads `PKFORGE_PAD_LAYOUT`; anything other than `xbox` is `Nintendo`.
  - `public sealed class PadRepeater(Action<PadButton> fire, Func<bool> shouldersRepeat) { public void Press(PadButton button, long nowMs); public void Release(PadButton button); public void Tick(long nowMs); }` with the Android timings: directions repeat after 320 ms then every 80 ms; L and R repeat after 380 ms then every 140 ms and only while `shouldersRepeat()` is true; no other button repeats.
  - `public sealed class SdlGamepadPump(Action<Action> postToMainThread, Action<SdlButton, bool> onButton, Action<float, float, float, float> onSticks, string libraryName = "libSDL2-2.0.so.0") { public bool Start(); public void Stop(); }`. It has no MAUI references. `Start()` loads the library with `NativeLibrary.TryLoad`, sets the hint `SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS` to `1`, calls `SDL_Init` with the game-controller flag only, opens every controller, and polls on a background thread. Button events reach `onButton` (pressed or released) and stick axes reach `onSticks`, both through `postToMainThread`. Axes are scaled to -1..1 with a dead zone of 0.2. Returns `false`, and logs once, when the library or `SDL_Init` is unavailable.

- [ ] **Step 1: Write the failing tests**

```csharp
// PadMappingTests
[Theory]
[InlineData(SdlButton.B, PadLayout.Nintendo, PadButton.A)]
[InlineData(SdlButton.A, PadLayout.Nintendo, PadButton.B)]
[InlineData(SdlButton.Y, PadLayout.Nintendo, PadButton.X)]
[InlineData(SdlButton.X, PadLayout.Nintendo, PadButton.Y)]
[InlineData(SdlButton.A, PadLayout.Xbox, PadButton.A)]
[InlineData(SdlButton.LeftShoulder, PadLayout.Nintendo, PadButton.L)]
[InlineData(SdlButton.RightShoulder, PadLayout.Xbox, PadButton.R)]
[InlineData(SdlButton.Back, PadLayout.Nintendo, PadButton.Select)]
[InlineData(SdlButton.Start, PadLayout.Nintendo, PadButton.Start)]
[InlineData(SdlButton.DpadLeft, PadLayout.Xbox, PadButton.Left)]
public void ResolvesButtons(SdlButton sdl, PadLayout layout, PadButton expected)
[Fact] public void GuideAndStickClicksAreIgnored()                     // Resolve returns null
[Fact] public void LayoutDefaultsToNintendo()                          // null, "", "weird" -> Nintendo; "xbox" -> Xbox

// PadRepeaterTests (fire appends to a list)
[Fact] public void DirectionFiresOnceThenRepeatsAfter320Then80()
    // Press(Down, 0) -> 1 fire; Tick(319) -> 1; Tick(320) -> 2; Tick(399) -> 2; Tick(400) -> 3
[Fact] public void ShoulderRepeatsAfter380Then140OnlyWhenAllowed()
    // allowed: Press(L, 0); Tick(380) -> 2; Tick(520) -> 3.  not allowed: Tick(1000) -> still 1
[Fact] public void ReleaseStopsTheRepeat()
[Fact] public void FaceButtonsNeverRepeat()                            // Press(A, 0); Tick(5000) -> 1

// SdlGamepadPumpTests
[Fact] public void PumpStartsWithoutSdlAndReportsUnavailable()
    // libraryName "libdoes-not-exist.so": Start() returns false and does not throw
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test ports/linux/PKForge.Linux.Tests --filter "FullyQualifiedName~PadMappingTests|FullyQualifiedName~PadRepeaterTests|FullyQualifiedName~SdlGamepadPumpTests"`
Expected: FAIL, types not defined.

- [ ] **Step 3: Implement `PadMapping`, `PadRepeater` and `SdlGamepadPump`.**
- [ ] **Step 4: Run the tests to verify they pass**

Run: the command from Step 2.
Expected: PASS.

- [ ] **Step 5: Start the pump** from `LinuxHosting` once the app has built. `postToMainThread` is `MainThread.BeginInvokeOnMainThread`. A pressed button goes through `PadMapping.Resolve` to `PadRepeater.Press`, a released one to `PadRepeater.Release` and `GamepadRouter.DispatchRelease`. `PadRepeater.fire` is `GamepadRouter.Dispatch`, `shouldersRepeat` is `router.TopPagesWithShoulders`, and a 16 ms dispatcher timer calls `PadRepeater.Tick`. `onSticks` is `GamepadRouter.DispatchStick`.
- [ ] **Step 6: Verify on the device**

Run the app on the device and press each button once on the Home screen.
Expected: d-pad moves the cursor, the button printed "A" opens, "B" goes back, holding a direction repeats. If A and B are swapped, set `PAD_LAYOUT=xbox` in `DECISIONS.md` and in the launcher.

- [ ] **Step 7: Commit**

```bash
git add ports/linux/PKForge.Linux ports/linux/PKForge.Linux.Tests ports/linux/DECISIONS.md
git commit -m "feat(linux): SDL2 gamepad input with hold-repeat"
```

### Task 10: On-screen keyboard

**Files:**
- Create: `ports/linux/PKForge.Linux/Core/OskModel.cs`
- Create: `ports/linux/PKForge.Linux/Platform/OskPage.cs`, `Platform/OskHook.cs`
- Modify: `ports/linux/PKForge.Linux/Hosting/LinuxHosting.cs`
- Test: `ports/linux/PKForge.Linux.Tests/OskModelTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class OskModel(string initial, int maxLength, bool numericOnly)`. Members: `string Text`, `(int Row, int Column) Cursor`, `bool Shifted`, `void Move(int dRow, int dColumn)` (wraps at the edges), `void PressKey()`, `void Backspace()`, `void ToggleShift()`, `string Confirm()` (returns `Text`), `string Cancel()` (returns `initial`). Layout: four rows, `1234567890`, `qwertyuiop`, `asdfghjkl-`, `zxcvbnm. '` where the last key of row four is a space; `numericOnly` leaves only the digit row. Typing past `maxLength` is ignored; `maxLength` 0 means no limit.
  - `OskPage : ContentPage, IPadHandler`: draws the text and the keys on an `SKCanvasView`; d-pad moves, A types, B is backspace, X toggles shift, Start confirms, Select cancels; touch types the key touched. `static Task<string> EditAsync(INavigation nav, string title, string initial, int maxLength, bool numericOnly)`.
  - `OskHook.Install()`: `Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("PKForgeLinuxOsk", ...)` and the same for `EditorHandler`, subscribing to each view's `Focused` event once. On focus it unfocuses the field, awaits `OskPage.EditAsync` with the field's `Text`, `MaxLength` and `Keyboard == Keyboard.Numeric`, sets `Text` to the result, and for an `Entry` calls `SendCompleted()` when the result was confirmed. Disabled when the environment variable `PKFORGE_OSK=0` is set, for desktop development with a real keyboard.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void TypingAppendsAndShiftUppercasesOneLetter()
    // cursor on 'q': PressKey -> "q"; ToggleShift; PressKey -> "qQ"; Shifted is false again
[Fact] public void MoveWrapsAtTheRowAndColumnEdges()
[Fact] public void BackspaceOnEmptyTextDoesNothing()
[Fact] public void TypingStopsAtMaxLength()                            // maxLength 3: four presses -> length 3
[Fact] public void NumericOnlyOffersOnlyDigits()
[Fact] public void CancelReturnsOriginalText()                         // initial "Pika", type two keys, Cancel() == "Pika"
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test ports/linux/PKForge.Linux.Tests --filter "FullyQualifiedName~OskModelTests"`
Expected: FAIL, type not defined.

- [ ] **Step 3: Implement `OskModel`.**
- [ ] **Step 4: Run the tests to verify they pass**

Run: the command from Step 2.
Expected: PASS, 6 tests.

- [ ] **Step 5: Implement `OskPage` and `OskHook`;** call `OskHook.Install()` from `LinuxHosting`.
- [ ] **Step 6: Verify under Xvfb** by opening a save, the editor, and the Nickname field with a pad script, typing two letters and confirming.

Expected: the screenshot shows the nickname changed. Also verify the live-filter fields (`PickerMenu` search, `BankSearchPage`): the list filters once the keyboard is confirmed.

- [ ] **Step 7: Commit**

```bash
git add ports/linux/PKForge.Linux ports/linux/PKForge.Linux.Tests
git commit -m "feat(linux): on-screen keyboard for text fields"
```

---

## Phase 4: Second screen

### Task 11: Output layout rules

**Files:**
- Create: `ports/linux/PKForge.Linux/Core/OutputLayout.cs`
- Create: `ports/linux/packaging/sway.conf`, `ports/linux/packaging/weston.ini`
- Test: `ports/linux/PKForge.Linux.Tests/OutputLayoutTests.cs`

**Interfaces:**
- Consumes: `OUTPUTS` and `COMPOSITOR` from `DECISIONS.md`.
- Produces:
  - `public sealed record OutputInfo(string Name, int Width, int Height); public static class OutputLayout { public const string MainTitle = "PKForge"; public const string LowerTitle = "PKForge Lower"; public static IReadOnlyList<OutputInfo> Parse(string swayOutputsJson); public static (OutputInfo Main, OutputInfo? Lower) Assign(IReadOnlyList<OutputInfo> outputs, string? mainName); }`. `Assign` puts the output named `mainName` (from `PKFORGE_MAIN_OUTPUT`) on top; with no name it takes the first output as main; with one output `Lower` is null.
  - `sway.conf` and `weston.ini`: both panels at 1024×768, the window titled `PKForge` fullscreen on the top panel, the window titled `PKForge Lower` fullscreen on the lower panel, each touch device mapped to its own panel, no bars, no decorations, screen blanking off.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void ParsesNamesAndCurrentModesFromSwayJson()            // fixture: two outputs at 1024x768
[Fact] public void NamedMainOutputGoesOnTop()
[Fact] public void HostIsUnavailableWithOneOutput()                    // Assign(...).Lower is null
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test ports/linux/PKForge.Linux.Tests --filter "FullyQualifiedName~OutputLayoutTests"`
Expected: FAIL, type not defined.

- [ ] **Step 3: Implement `OutputLayout` and write both compositor configs** using the output names recorded in `DECISIONS.md`.
- [ ] **Step 4: Run the tests to verify they pass**

Run: the command from Step 2.
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add ports/linux/PKForge.Linux ports/linux/PKForge.Linux.Tests ports/linux/packaging
git commit -m "feat(linux): output layout rules and compositor configs"
```

### Task 12: `LinuxSecondaryDisplayHost`

**Files:**
- Create: `ports/linux/PKForge.Linux/Platform/LinuxSecondaryDisplayHost.cs`
- Modify: `ports/linux/PKForge.Linux/Hosting/LinuxHosting.cs`

**Interfaces:**
- Consumes: `OutputLayout`, `MULTI_WINDOW` from `DECISIONS.md`, the shared `SecondScreenBoxPage`, `PokeparkJournalPage`, `SecondScreenMode.Allowed`.
- Produces: `public sealed class LinuxSecondaryDisplayHost(IServiceProvider services) : ISecondaryDisplayHost`.
  - `IsAvailable` is `SecondScreenMode.Allowed` and a lower output exists (asked from the compositor once at start and cached; on sway through `swaymsg -t get_outputs -r`, otherwise from the environment variable `PKFORGE_LOWER_OUTPUT` set by the launcher).
  - `Arrangement` is `ScreenArrangement.Unknown`, because both panels are the same size, which makes the app say "the main screen" and "the second screen".
  - `ShowAsync` resolves a `SecondScreenBoxPage` from `services`, as the Android host does, and opens it with `Application.Current.OpenWindow(new Window(page) { Title = OutputLayout.LowerTitle })`. A second call while the window is open does nothing.
  - `ShowPokeparkJournalAsync` and `RefreshPokeparkJournalAsync` mirror the Android host's behaviour with the journal page.
  - `DismissAsync` closes the window with `Application.Current.CloseWindow`.
  - **If `MULTI_WINDOW=no`:** `IsAvailable` returns `false` and nothing else is implemented. Record in `DECISIONS.md` that the second screen is deferred until OpenMaui gains multi-window support, and skip Steps 2 and 3.

- [ ] **Step 1: Implement the host and register it** as `ISecondaryDisplayHost` (singleton), replacing the placeholder.
- [ ] **Step 2: Verify placement in a headless compositor with two outputs**

Run: `WLR_BACKENDS=headless WLR_HEADLESS_OUTPUTS=2 WLR_LIBINPUT_NO_DEVICES=1 sway -c ports/linux/packaging/sway.conf &` then start the app inside it with a pad script that opens a save, then `swaymsg -t get_tree`.
Expected: the tree shows a window titled `PKForge` on the first output and `PKForge Lower` on the second, both fullscreen.

- [ ] **Step 3: Verify on the device:** open a box; the lower panel shows the inspector and follows the cursor; touching the lower panel does not take the gamepad away from the top panel; closing and reopening the lid leaves both panels working.
- [ ] **Step 4: Commit**

```bash
git add ports/linux/PKForge.Linux ports/linux/DECISIONS.md
git commit -m "feat(linux): lower screen as a second window"
```

---

## Phase 5: Remaining gaps

### Task 13: Known feature gaps, and the one optional edit under `src/`

These are the behaviours the zero-edit container cannot give. Each is decided here so nobody rediscovers them.

| Gap | Cause | Decision |
|---|---|---|
| Touch on Home's save cards (tap and long press) does nothing | `HomePage.AttachPress` (`src/PKForge.App/Views/HomePage.cs:189`) wires the Android touch stream only | Ship with gamepad-only cards. **Optional edit:** add a non-Android branch to `AttachPress` that attaches a `TapGestureRecognizer` for the tap and a pointer-pressed timer of 500 ms for the long press. This is the only planned candidate for `INTRUSIONS.md`. Ask the user before making it. |
| Home-screen widgets, launcher shortcuts | Android concepts | Inert. The "Set as widget partner" entries stay in menus and answer "This Pokémon can't be a partner." |
| Pokéwalker step counting | No pedometer | `WalkerSteps` already reports no sensor off Android; the app falls back to its stroll mode. |
| In-app updates | APK installer | Disabled by the stand-in. Updates come through the port package. |
| About-screen easter egg | Android `MediaPlayer` | Inert. |
| Background music | Android `MediaPlayer` | Silent stand-in until Task 14. |
| Keep-screen-on in the Poképark | Android window flag | Screen blanking is turned off in the compositor config for the whole session. |
| Rumble on handled input | Android haptics | None. |

- [ ] **Step 1: Create `ports/linux/INTRUSIONS.md`** with the heading and the sentence "No file under `src/` has been edited for the Linux port."
- [ ] **Step 2: Copy the table above into `ports/linux/README.md`** under a new heading "Known gaps on Linux".
- [ ] **Step 3: Ask the user** whether to make the optional `AttachPress` edit. If yes, make it, record file, lines and reason in `INTRUSIONS.md`, and re-run both existing test projects.
- [ ] **Step 4: Commit**

```bash
git add ports/linux/INTRUSIONS.md ports/linux/README.md
git commit -m "docs(linux): known gaps and intrusion ledger"
```

### Task 14 (optional): Real music playback

Skip unless the user asks for it. Replace the body of `Seams/MusicPlayer.cs` with playback through SDL2_mixer (`libSDL2_mixer-2.0.so.0`) if the recon found it, keeping the same public members. The library list, order, autostart and per-place sources persist through `Preferences` under the keys the Android player uses, so an imported data archive keeps the user's settings.

---

## Phase 6: Performance on 1 GB

### Task 15: Launcher, publish settings and measurements

**Files:**
- Create: `ports/linux/packaging/PKForge.sh`, `ports/linux/packaging/build.sh`, `ports/linux/packaging/port.json`
- Modify: `ports/linux/PKForge.Linux/PKForge.Linux.csproj`
- Modify: `ports/linux/DECISIONS.md`

**Interfaces:**
- Consumes: every key in `DECISIONS.md`.
- Produces:
  - `build.sh`: runs `dotnet publish ports/linux/PKForge.Linux -c Release -r linux-arm64 --self-contained -p:PublishReadyToRun=true` (or `linux-musl-arm64`), then assembles `dist/PKForge/` holding the publish output, `PKForge.sh`, the compositor config, `port.json`, `LICENSE` and the attribution files.
  - `PKForge.sh`: sets `XDG_DATA_HOME`, `XDG_CONFIG_HOME` and `XDG_CACHE_HOME` to `data/`, `config/` and `cache/` inside the port folder, so the Bank and backups live on the games card and survive a firmware reflash; exports `PKFORGE_PAD_LAYOUT`, `PKFORGE_SAVE_ROOTS`, `PKFORGE_MAIN_OUTPUT`, `PKFORGE_LOWER_OUTPUT`, `OPENMAUI_RENDERER` from the recorded decisions; exports `DOTNET_gcConcurrent=0`, `DOTNET_GCConserveMemory=7`, `DOTNET_TieredPGO=0`, `DOTNET_GCHeapHardLimit=0x25800000` (600 MB); uses the running compositor when one exists, otherwise starts the bundled weston with `weston.ini` and runs the app inside it; writes the app's stdout and stderr to `log.txt` in the port folder.

**Budgets** (proposed here; confirm or change them with the user after the first measurements):

| Measure | Budget |
|---|---|
| Cold start to Home | 15 s |
| Resident memory with a Gen 9 save open, the Bank visited and the Poképark visited | 450 MB |
| Poképark frame rate | 30 fps |
| Box cursor move to redraw | 50 ms |

- [ ] **Step 1: Write `build.sh`, `PKForge.sh` and `port.json`.**
- [ ] **Step 2: Build and install**

Run: `ports/linux/packaging/build.sh` on a Linux machine, then copy `dist/PKForge/` to the device's ports folder.
Expected: the folder is under 400 MB and the app appears in the firmware's ports list.

- [ ] **Step 3: Measure on the device** with `OPENMAUI_RENDER_STATS=1`: time from launch to Home; `ps -o rss=` after opening a Gen 9 save, the Bank and the Poképark; the frame rate the render stats print in the Poképark.
- [ ] **Step 4: Record the four numbers in `DECISIONS.md` under `MEASURED`.** For each one over budget, apply these in order and re-measure after each:
  1. Poképark too slow or too heavy: set the preference `pkforge.pokepark.renderer.v1` to `cpu`, or `OPENMAUI_RENDERER=raster`, whichever the device runs better.
  2. Memory: add `DOTNET_GCgen0size=0x1000000` (a 16 MB young generation, so the heap grows in smaller steps). Lower `DOTNET_GCHeapHardLimit` only if the kernel is killing the app.
  3. Start-up: confirm the publish is ReadyToRun (`PublishReadyToRun` in the csproj) and that the port folder is on the faster card.
- [ ] **Step 5: If any budget is still missed, stop and report the numbers to the user.** Deeper fixes (changing the sprite cache size, trimming assets) need edits under `src/` and are the user's call.
- [ ] **Step 6: Commit**

```bash
git add ports/linux/packaging ports/linux/PKForge.Linux/PKForge.Linux.csproj ports/linux/DECISIONS.md
git commit -m "feat(linux): launcher, publish settings and device measurements"
```

---

## Phase 7: Keeping it working

### Task 16: CI and the upstream-merge routine

**Files:**
- Create: `.github/workflows/linux-port.yml`
- Modify: `ports/linux/README.md`
- Test: `ports/linux/PKForge.Linux.Tests/SeamGuardTests.cs`

**Interfaces:**
- Produces: a workflow that runs on pushes and pull requests touching `src/**` or `ports/linux/**`, on `ubuntu-latest`, with submodules: restore, `dotnet test ports/linux/PKForge.Linux.Tests`, `dotnet build ports/linux/PKForge.Linux -r linux-x64`, install `xvfb` and `imagemagick`, run `ports/linux/packaging/smoke.sh`, upload `smoke.png`. It does not touch `build.yml`.

- [ ] **Step 1: Write the failing tests in `SeamGuardTests`**

```csharp
[Fact] public void SharedMauiProgramStillCallsUseSkiaSharpOnTheBuilder()
    // src/PKForge.App/MauiProgram.cs contains "UseMauiApp<App>().UseSkiaSharp()"
    // and its file-scoped namespace is "PKForge.App": the hosting seam depends on both
[Fact] public void ReplacedFilesStillExistUpstream()
    // each of the four excluded paths exists, so a rename upstream is noticed
[Fact] public void NoNewUnguardedAndroidFileAppeared()
    // scan src/PKForge.App (outside Platforms/) with the #if-tracking rule from the analysis;
    // the set of files with unguarded Android references equals the eleven listed in this plan
```

- [ ] **Step 2: Run them**

Run: `dotnet test ports/linux/PKForge.Linux.Tests --filter "FullyQualifiedName~SeamGuardTests"`
Expected: the first two PASS at once (they pin today's state); the third FAILS until the scanner is written.

- [ ] **Step 3: Implement the scanner inside the test file** (preprocessor state per line, `#if ANDROID` regions skipped, comments and string literals ignored).
- [ ] **Step 4: Run the tests to verify they pass**

Run: the command from Step 2.
Expected: PASS, 3 tests.

- [ ] **Step 5: Write the workflow.**
- [ ] **Step 6: Add "Merging upstream" to `ports/linux/README.md`:** merge upstream; run the Linux tests; a failing `AssetParityTests` means copy the new asset lines into `Assets.props`; a failing `SeamGuardTests` names the new Android-only file or the changed call; a build error names the member to add to a stand-in or to the stub layer.
- [ ] **Step 7: Commit**

```bash
git add .github/workflows/linux-port.yml ports/linux/PKForge.Linux.Tests ports/linux/README.md
git commit -m "ci(linux): build, test and smoke the Linux head; seam guards"
```

---

## Open risks

| Risk | Where it is settled | If it goes wrong |
|---|---|---|
| The chosen firmware has no window system | Task 1 (`COMPOSITOR`) | The launcher bundles weston (PortMaster's westonpack). |
| OpenMaui cannot open a second window | Task 1 (`MULTI_WINDOW`) | Single-screen release; the second screen waits for OpenMaui. |
| 1 GB of RAM is not enough | Task 1 (`IDLE_RSS_MB`), Task 15 | Stop and report. This is the likeliest blocker. |
| A Chrome painter uses a SkiaSharp 3 API removed in 4 | Task 2 | Needs an edit under `src/PKForge.Chrome` or a different OpenMaui version: the user's call. |
| The hosting seam stops binding after an upstream change to `MauiProgram.cs` | Task 16 (`SeamGuardTests`) | Fall back to a Linux-owned composition root that repeats the service list. |
| `InvariantGlobalization` changes sorting or number formatting somewhere visible | Task 5, Task 15 | Bundle ICU with the app instead (`Microsoft.ICU.ICU4C.Runtime`) and turn the setting off. |
| OpenMaui is young and has bugs in less-used controls | Tasks 5, 8, 10 | Work around inside the container, or report upstream to OpenMaui. |

An alternative worth one hour before starting: GammaOS Nano is described as an Android-based firmware for this device. If it runs the stock APK acceptably in 1 GB, no port is needed. Upstream already handles the original RG DS on GammaOS.
