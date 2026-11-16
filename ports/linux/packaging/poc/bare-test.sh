#!/bin/bash
# Proof of concept: run the self-contained app with ONLY bundled libraries on a system that has a
# Wayland compositor (Weston) and nothing else of ours: no .NET, no GTK, no SDL2.
# Run inside a fresh ubuntu:24.04 arm64 container:  bare-test.sh <poc-dir>
set -uo pipefail
POC="$1"
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq >/dev/null
apt-get install -y -qq --no-install-recommends weston >/dev/null 2>&1
echo "system has: glibc $(ldd --version | head -1 | awk '{print $NF}'), weston $(weston --version 2>&1 | awk '{print $2}')"
echo "system gtk-3 libs: $(ldconfig -p | grep -c 'libgtk-3')   SDL2: $(ldconfig -p | grep -c 'libSDL2')   dotnet: $(command -v dotnet >/dev/null && echo yes || echo no)"

export XDG_RUNTIME_DIR=/tmp/xdgrt; mkdir -p "$XDG_RUNTIME_DIR"; chmod 700 "$XDG_RUNTIME_DIR"
weston --backend=headless --width=1024 --height=768 --socket=wl-poc --idle-time=0 >/tmp/weston.log 2>&1 &
sleep 3

D=/tmp/run; mkdir -p "$D"
env -i HOME=/root PATH=/usr/bin:/bin XDG_RUNTIME_DIR="$XDG_RUNTIME_DIR" WAYLAND_DISPLAY=wl-poc \
  XDG_DATA_HOME="$D/data" XDG_CONFIG_HOME="$D/config" XDG_CACHE_HOME="$D/cache" \
  LD_LIBRARY_PATH="$POC/lib" NO_AT_BRIDGE=1 GDK_BACKEND=wayland OPENMAUI_RENDERER="${POC_RENDERER:-auto}" DOTNET_gcConcurrent=0 DOTNET_GCConserveMemory="${POC_CONSERVE:-0}" \
  "$POC/app/PKForge" >"$D/out.txt" 2>&1 &
PID=$!
sleep 25
if kill -0 "$PID" 2>/dev/null; then
  echo "RESULT: app alive after 25 s"
  grep -oE '/[^ ]+\.so[^ ]*' "/proc/$PID/maps" | sort -u > "$D/maps.txt"
  echo "libraries loaded from the bundle: $(grep -c "$POC/lib" "$D/maps.txt")   from the app folder: $(grep -c "$POC/app" "$D/maps.txt")"
  echo "libraries loaded from the SYSTEM:"; grep -v "$POC" "$D/maps.txt" | sed 's|.*/||' | tr '\n' ' '; echo
  echo "resident memory: $(awk '/VmRSS/{print $2/1024 " MB"}' "/proc/$PID/status")"
  kill "$PID"
else
  echo "RESULT: app DIED"
fi
echo "--- app log:"; cat "$(find "$D/data" -name pkforge.log 2>/dev/null | head -1)" 2>/dev/null | tail -8
echo "--- stdout/stderr (filtered):"; grep -vE 'Draw #' "$D/out.txt" | grep -iE 'error|exception|warn|fail|not found|cannot|gtk|font' | sort | uniq -c | sort -rn | head -20
