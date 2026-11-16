# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

PKForge is a Pokémon save editor and cross-generation Bank for Android (.NET MAUI), tuned for dual-screen gamepad handhelds (AYN Thor). It is built on PKHeX.Core with the Auto Legality Mod (ALM) compiled in, so legalizing works offline. GPLv3.

## Build and test

Requires the .NET 10 SDK, the `maui-android` workload, and the Android SDK (API 36). The submodules must be present, because Engine, AutoMod and the App's assets all read from `external/`.

```bash
git submodule update --init --recursive
dotnet test tests/PKForge.Domain.Tests/PKForge.Domain.Tests.csproj
dotnet test tests/PKForge.Engine.Tests/PKForge.Engine.Tests.csproj
dotnet build src/PKForge.App/PKForge.App.csproj -f net10.0-android

# single test class / method (xunit)
dotnet test tests/PKForge.Engine.Tests/PKForge.Engine.Tests.csproj --filter "FullyQualifiedName~SoulGoldTests"
dotnet test tests/PKForge.Engine.Tests/PKForge.Engine.Tests.csproj --filter "FullyQualifiedName=PKForge.Engine.Tests.PartyTests.PartyReordersLikeTheGames"

# sideloadable diagnostic build: separate app id (org.pkforge.app.debug), defines DIAGNOSTIC
dotnet build src/PKForge.App/PKForge.App.csproj -f net10.0-android -p:DiagnosticBuild=true
```

- `Directory.Build.props` sets `TreatWarningsAsErrors=true` and nullable for every project. A warning breaks the build. `PKForge.AutoMod` and `tools/ChromePreview` are the exceptions.
- CI (`.github/workflows/build.yml`) restores `PKForge.sln`, runs both test projects in Release, and builds the APK. A `v*` tag builds a signed Release APK and publishes a GitHub release. The version is set by `ApplicationVersion`/`ApplicationDisplayVersion` in `PKForge.App.csproj`.
- Many Engine tests load real saves from `.local-testdata/` at the repo root (gitignored, e.g. `.local-testdata/romhacks/soulgold.sav`, `.local-testdata/thor/retroarch/...`). Some use `PKFORGE_LUMI_FIXTURE`. **They pass silently by skipping when the file is missing**, so a green run on a fresh checkout does not exercise ROM-hack or real-save paths.
- Only the App is Android-specific. Domain, Engine, Infrastructure and Chrome are plain `net10.0` and can be built and tested on macOS or Linux.

## Architecture

The layers depend in one direction. Keep them that way:

| Project | Role | May depend on |
|---|---|---|
| `src/PKForge.Domain` | Contracts (`Contracts.cs`: `ISaveEngine`, `ISafeSaveWriter`, `IBankService`, `ISaveFileAccess`, …), DTOs, and pure logic (Poképark simulation `ParkLife.*`, dex tracking, bank organization, focus navigation). No PKHeX, no MAUI, no Android. | nothing |
| `src/PKForge.Engine` | The only layer that touches PKHeX.Core and ALM. `SaveEngine`/`SaveEngineSession` implement `ISaveEngine`, and each feature is a `*Service` adapter. One subfolder per ROM hack (`Unbound/`, `RadicalRed/`, `Expansion/`, `SoulGold/`, …) holds its own layout code plus vendored data tables, embedded as resources (see the csproj for provenance). | Domain, PKHeX.Core, AutoMod |
| `src/PKForge.AutoMod` | A shim that compiles `external/PKHeX-Plugins/PKHeX.Core.AutoMod/**/*.cs` against the in-repo PKHeX.Core submodule, so ALM and the engine always share one PKHeX revision. Do not modify upstream files. | PKHeX.Core |
| `src/PKForge.Infrastructure` | File-backed implementations: `FileBankService`, `FileBackupService`, `SafeSaveWriter`, `AtomicFile`, `SaveSessionService`, PKSM bank import, folder mirror. | Domain |
| `src/PKForge.Chrome` | The design system: tokens and ~50 pure-SkiaSharp `*Paint` painters. No MAUI. | Domain, SkiaSharp |
| `src/PKForge.App` | MAUI head (`net10.0-android`). `MauiProgram.cs` is the composition root: it registers every service as a DI singleton and roots data stores under `FileSystem.AppDataDirectory`. | everything |

### Save write safety (do not bypass)
Every save write goes through `ISafeSaveWriter` (`Infrastructure/SafeSaveWriter.cs`). It validates the candidate bytes, creates a backup through `IBackupService`, then performs one platform write through `ISaveFileAccess`. Writes are limited to a `WriteScope` (the slots actually touched). Saves with a suspicious layout (`LayoutRisk`) are refused or need explicit confirmation, which is persisted in `ISaveIdentityStore`. New features that modify a save must use this path. They must never write files directly.

### App UI
- The views are built in code (no XAML). Most screens are `SKCanvasView`s that call Chrome painters, inside MAUI layouts. The large screens are split into partial classes (`BoxBrowserPage.*.cs`, ~6k lines total; `BankPage.cs`; `MonSummaryView.cs`).
- Gamepad input: `Services/GamepadRouting.cs` defines `PadButton` and the `IPadHandler` / `IPadReleaseHandler` / `IPadStickHandler` / `IPadPagingHandler` interfaces. `GamepadRouter.Dispatch` sends each press to the visible screen. Every screen must work with gamepad and with touch.
- Second screen: `ISecondaryDisplayHost` (Domain) plus `SecondScreenBoxPage`/`SecondScreenState` show the inspector, art, dex or journal on the second display.
- Platform code lives in `Platforms/Android/` (SAF file access, pickers, emulator scanner, widgets, music) and is registered under `#if ANDROID` in `MauiProgram.cs`. About a dozen other files also contain `#if ANDROID` blocks (`App.cs`, `Kit.cs`, `HomePage.cs`, `AppLog`, `PerfTrace`, `VsyncLoop`, `WalkerSteps`, `AppUpdateService`, …).
- Assets are `MauiAsset` items with explicit `LogicalName`s (`sprites/`, `artwork/`, `showdown/front/`, `ui/pksm/`, `events/`, …), many globbed straight from `external/PKHeX/PKHeX.Drawing.*`. They are read with `FileSystem.OpenAppPackageFileAsync("<logical name>")`.

### Generated data (regenerate, don't hand-edit)
- `src/PKForge.Domain/Resources/*.tsv(.gz)`: `tools/DexFacts/build.py`, `tools/DexFacts/evolutions.py`, `tools/SpriteForms/build.py`, `tools/SpriteForms/pmd.py`. These scripts produce deterministic, byte-identical output.
- `src/PKForge.App/Resources/Events/events-g*.bin.gz`: `tools/EventGen/pack.py`.
- `src/PKForge.Engine/GsChronicles/Data/*`: `tools/GscTables/generate.py`.
- `tools/ChromePreview`: renders Chrome painters to PNG off-device (`dotnet run --project tools/ChromePreview`; `-- icons` regenerates the `px_*` pixel icons in `Resources/UI/pksm`).
- `tools/DexGen` and `tools/SpritePack` are C# generators that run against Engine.

## Submodules
- `external/PKHeX` is the PKForge fork of PKHeX (`sofianeelhor/PKForge-PKHeX`). Engine fixes sometimes land there as their own commits.
- `external/PKHeX-Plugins` is ALM (santacrab2), compiled through the AutoMod shim.

## Linux port (branch `linux-port`, untracked `ports/`)
`ports/linux/README.md` records a spike that runs real App views on Linux through OpenMaui, and the plan for the full port. `ports/linux/SkiaSpike/SkiaSpike.csproj` links selected `src/PKForge.App` files unchanged (`-p:PKForge=<path>` points it at another checkout). It needs MAUI 10.0.130 and SkiaSharp 4.152.1, while the main app pins MAUI 10.0.20 and SkiaSharp 3.116.1. Mixing them fails restore with NU1605. Build it with `dotnet build -r linux-x64 --no-self-contained` and run it under X11/Xvfb with `MAUI_PREFER_X11=1`.

## Conventions
- Commit subjects use a lowercase prefix: `release: vX.Y.Z, <summary>`, `fix:`, `test:`, `docs:`.
- Code comments explain *why*, in prose, and often carry provenance or licensing for vendored data. Keep attribution notes (`ATTRIBUTION.md`, data `README.md`s, csproj comments) accurate when you add assets or tables.
