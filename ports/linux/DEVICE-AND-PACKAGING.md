# RG DS Plus: what the device ships, and how PKForge should install

Written 2026-11-15. Nothing here was checked on a physical RG DS Plus. Device facts come from public sources (linked at the end). The measurements were taken in Linux arm64 containers on a Mac.

## 1. What the device ships

### Hardware (confirmed by Anbernic and several reviews)

- Rockchip RK3568, 1 GB RAM, Mali-G52 GPU.
- Two 4.3" 1024×768 touch panels.
- No internal storage. Two microSD slots: TF1/INT holds the OS and is the boot card; TF2 is for games.
- Linux only.

### Stock firmware (V1.0, 2026-09-15)

Reported by community projects that run on it. Confidence is noted per row.

| Area | What it ships | Source | Confidence |
|---|---|---|---|
| Base | Buildroot 2024.02, Linux 6.1 | rgds-terminal | Good (one source, tested on device) |
| Start-up | `/etc/init.d/rcS` runs scripts in sequence; the vendor launcher is started by `/mnt/vendor/ctrl/loadapp.sh` | dsboot | Good |
| Display | **Weston (Wayland compositor)**, the two panels as two separate outputs | rgds-terminal | Good |
| C library | glibc, newer than Debian 12's (2.36): binaries built on Debian 12 run on it | rgds-terminal | Good |
| GPU driver | A 54 MB graphics driver file is loaded at start, which suggests ARM's Mali driver, not Mesa | dsboot | Inferred, not stated |
| SDL2 | Present; stock apps and community apps use it | rgds-terminal, DSFetch | Good |
| Fonts | DejaVu and Source Han Sans (covers Chinese, Japanese, Korean) | rgds-terminal | Good |
| Shell and tools | bash, Python 3, SSH with root login, gzip, squashfs tools | dsboot, rgds-terminal | Good |
| Input | Buttons come from the input device `ANBERNIC-rk3568-keys` | rgds-terminal, PortMaster gist | Good |
| GTK 3 | Not mentioned by any source | — | Unknown. Assume absent. |
| .NET | Not shipped. One community port (Bastion) bundles its own .NET runtime and runs. | Bastion-RGDSPlus | Good |

Behaviours of the stock firmware that affect us:

- **Apps menu.** Every `.sh` file in `Roms/APPS` on the card appears in the Apps menu and is run as a script. Icons are `Roms/APPS/Imgs/<name>.png`. TF1's folder is `/mnt/mmc/Roms/APPS`. The same folder on TF2 is also listed (reported, only TF1 was tested by that source).
- **Ports menu.** A separate `Ports` folder (`/mnt/mmc/Ports`, and `/mnt/sdcard/Ports` in one port's instructions) with the same script convention.
- **Two screens.** An app gets both screens by opening one window per output. rgds-terminal does this through SDL2.
- **Touch quirk.** Touches on the bottom panel are delivered to the *top* window, with coordinates that cover the bottom panel.
- **GPU quirk.** With SDL2, render-target textures in one window corrupted the other window's output.
- **Updater hazard.** A popular stock OS modification's updater clears `Roms/APPS` on TF1. Its author advises against installing there. Anything kept only in that folder can be lost.

Not found in any source: the Weston version and shell, whether XWayland is present, the exact path where TF2 mounts, whether the cards are exFAT or FAT32, swap or zram, and the environment the Apps menu gives a script.

### Other firmwares

| Firmware | Display stack | Consequence for PKForge |
|---|---|---|
| Stock (Anbernic) | Weston, two outputs | Works with what we have. Primary target. |
| BananaLinux | No compositor; its menu owns the panels through KMS and hands them to games. Mesa Panfrost. Read-only system, exFAT data partition, no udev. Supports PortMaster. | We would have to bring a compositor, and the lack of udev makes that hard. Not a first target. |
| ROCKNIX, KNULLI | Not documented for this device in anything I found | Unknown |
| GammaOS Nano | Described by one article as Android-based | Would run the Android APK instead of this port |
| "Dedicated OS" | A pared-down stock firmware that runs `main.sh` from the second card | Single-purpose images; not a general install target |

## 2. What our app needs (measured)

Measured on the current head (commit 30dbfde), self-contained Release build for `linux-arm64`.

| Measurement | Result |
|---|---|
| App runs on Weston through native Wayland | Yes. OpenMaui auto-detects Wayland, and no X11 library is loaded. Until now only X11 had been tested. |
| Published app folder | 265 MB, 7,319 files. No symbolic links. No two names that differ only by case. No characters exFAT rejects. |
| Native libraries the app needs beyond .NET | 78, 37 MB. GTK 3 and its dependencies, SDL2 and its dependencies, fontconfig and FreeType, X11 client libraries that GTK links against. |
| Download size | About 155 MB zipped (fast compression; the test archive held each library twice, so the real figure is a little lower). |
| **Bundle test on a bare system** | **Pass.** A fresh Ubuntu 24.04 container with only Weston installed (no .NET, no GTK, no SDL2). The app started, reached Home, and started the gamepad reader, using 78 bundled libraries. |
| Libraries taken from the system in that test | Only the C library family, the C++ runtime, the GPU driver stack (EGL, GBM, DRM, and the software-GL driver's own dependencies), the Wayland client libraries and udev. |
| Memory at Home, software GL | 309 MB. This includes the container's software GL driver, which the device does not use. |
| Memory at Home, CPU renderer, memory-saving GC settings | 164 MB |

The libraries were harvested from Debian 12, whose C library is older than the device's, so they should load there. That was tested only against a newer C library in a container.

The memory figures are for the Home screen with no save open. The device's GPU driver will add its own cost, which is unknown.

## 3. Recommended install

**Target the stock firmware first, as one zip that the user extracts to the root of a card.** No installer, no network, nothing written to the system partition. This is the convention the existing community apps use.

```
Roms/APPS/PKForge.sh          the Apps menu entry (launcher script)
Roms/APPS/Imgs/PKForge.png    its icon
Roms/APPS/PKForge/app/        the self-contained .NET app and its assets
Roms/APPS/PKForge/lib/        every bundled native library, as plain files named by SONAME
Roms/APPS/PKForge/etc/        fonts.conf and other config the bundled libraries read
Roms/APPS/PKForge/LICENSES/   GPLv3, attributions, licences of the bundled libraries
PKForge/                      the user's data: Bank, backups, settings, logs, exports
```

Decisions behind that layout:

1. **User data lives outside `Roms/APPS`**, in `PKForge/` at the card root. The launcher works out the card root from its own location. An updater that clears `Roms/APPS`, or the user replacing the app folder with a new version, then never touches the Bank.
2. **Recommend the TF2 (games) card**, with TF1 also working. TF2 is the card the user already manages from a computer, and it is not the one the stock updater touches.
3. **Bundle everything except what must match the device.** Bundled: .NET, GTK 3 and its dependency tree, fontconfig, FreeType, SDL2. Never bundled: the C library family, the C++ runtime, EGL/GLES/GBM/DRM, the Wayland client libraries, udev. Those must be the device's own, because the GPU driver and the compositor are built against them.
4. **Prefer the device's SDL2 for the gamepad when it exists**, and fall back to the bundled copy. The device's copy is the one known to see its buttons. The launcher decides which folder to put on the library path.
5. **Plain files only.** No symbolic links and no reliance on Unix permissions inside the package, because the card may be exFAT or FAT32. The launcher marks the app executable itself if the filesystem allows it.
6. **Updates are a new zip** extracted over the old one. The data folder is untouched.

What the launcher script has to do:

- Set the library path to the bundled `lib/` folder.
- Point the app's data, config and cache folders at `PKForge/` on the card.
- Point fontconfig at the bundled `fonts.conf`, which lists the device's font folder so Japanese, Chinese and Korean names render.
- Tell GTK to use Wayland and to skip its accessibility bus, which the device does not run.
- Pick the renderer (GPU or CPU) and set the memory-saving runtime settings.
- Make sure the bottom screen and its touch panel are on, as rgds-terminal's launcher does, and restore them on exit.
- Write the app's output to a log file in the data folder.

### Options considered and not chosen

| Option | Why not |
|---|---|
| AppImage (OpenMaui's own packaging tool) | It bundles .NET but not GTK, which it expects from a desktop system. It also needs FUSE or a self-extract step. No benefit on a handheld. |
| PortMaster port | PortMaster is not on the stock firmware by default; getting it there needs manual patching. It fits firmwares like BananaLinux, but those have no compositor, which is the harder problem. Worth doing later as a second package. |
| A `.deb` or other system package | Buildroot has no package manager. |
| The stock OS modification's Software Center | It is a third-party store with install scripts. We could submit to it once the zip exists; it is not a replacement for it. |
| Installing into the system partition | Risky, lost on reflash, and unnecessary. |

## 4. What still has to be checked on a device

In order of how much they could change the plan:

1. **Memory.** Start the app, open a save, the Bank and the Poképark, and read the resident memory. This decides whether the GPU renderer is affordable or the CPU renderer is needed.
2. **The GPU path.** Whether OpenMaui's GPU renderer works with the device's driver at all, given the render-target quirk rgds-terminal hit. The CPU renderer is the fallback, and its speed at 1024×768 on this CPU is unknown.
3. **Two windows.** Whether OpenMaui can open a second window and put each one fullscreen on a chosen output. This is still untested even in a container.
4. **Touch routing.** Bottom-panel touches arrive at the top window. The second-screen design must route them.
5. **Whether the bundled GTK starts against the device's Weston.** It did against Weston 13 in the container.
6. **The card's filesystem and the TF2 mount path**, to confirm the launcher's assumptions.
7. **Gamepad through SDL2**, and which layout (Nintendo or Xbox) the device reports.

A short script that collects items 5 to 7 plus library and version listings should be the first thing run on a device.

## 5. Reproducing the measurements

The two scripts are in `ports/linux/packaging/poc/`.

```bash
# 1. Publish (in the development container)
dotnet publish ports/linux/PKForge.Linux -c Release -r linux-arm64 --self-contained -o ports/linux/dist/poc/app
# 2. Harvest the libraries from Debian 12
docker run --rm -v "${PWD}:${PWD}" debian:12 bash "${PWD}/ports/linux/packaging/poc/collect-libs.sh" "${PWD}/ports/linux/dist/poc/app" "${PWD}/ports/linux/dist/poc/lib"
# 3. Run on a bare system that has only Weston
docker run --rm -v "${PWD}:${PWD}:ro" ubuntu:24.04 bash "${PWD}/ports/linux/packaging/poc/bare-test.sh" "${PWD}/ports/linux/dist/poc"
```

Write the paths as `${PWD}` with braces: zsh reads `$PWD:ro` as a modifier and mangles the path.

`collect-libs.sh` currently copies each library twice (once under its SONAME, once under its full name). The real package should keep only the SONAME copy.

## Sources

- [Anbernic: RG DS Plus vs RG DS](https://anbernic.com/blogs/news/rg-ds-plus-vs-rg-ds)
- [rgds-terminal](https://github.com/NilsSlui/rgds-terminal): stock firmware base, Weston, two outputs, touch and GPU quirks, install layout
- [dsboot](https://github.com/abradburne/dsboot): stock start-up sequence, vendor paths, the 54 MB graphics driver
- [DSFetch](https://github.com/Misaka0x2730/dsfetch): Ports and Apps layout, the updater hazard
- [Bastion-RGDSPlus](https://github.com/PolluxPT/Bastion-RGDSPlus): a port that bundles its own .NET runtime on the stock firmware
- [PortMaster on the official RG DS Linux firmware (gist)](https://gist.github.com/omerkot/f5b80769561db5283d254fb5d4fcaa45)
- [REGTA port](https://github.com/lualiliu/REGTA-RGDSPLUSPort-Complete): the `/mnt/sdcard/Ports` path
- [Stock OS modification toolkit](https://github.com/cbepx-me/Anbernic-RK3568-RGdsxx-StockOS-Modification)
- [voidien/rg-ds-plus](https://github.com/voidien/rg-ds-plus): firmware unpacking tool and dual-screen examples
- [BananaLinux](https://github.com/JorreFog/BananaLinux) and its [architecture notes](https://raw.githubusercontent.com/JorreFog/BananaLinux/main/docs/ARCHITECTURE.md)
- [dedicated-os/main-rgds-plus](https://github.com/dedicated-os/main-rgds-plus)
- [OpenMaui AppImage packaging guide](https://github.com/open-maui/appimage/blob/main/docs/PACKAGING-GUIDE.md)
