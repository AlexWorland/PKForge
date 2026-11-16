#!/bin/bash
# Proof of concept: harvest the native libraries the PKForge Linux head needs from Debian 12
# (glibc 2.36, older than the RG DS Plus stock firmware's), as real files named by SONAME
# (no symlinks: the SD card is exFAT). Run inside a debian:12 arm64 container.
#   collect-libs.sh <app-dir> <out-lib-dir>
set -euo pipefail
APP="$1"; OUT="$2"
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq >/dev/null
apt-get install -y -qq --no-install-recommends libgtk-3-0 libsdl2-2.0-0 libfontconfig1 fontconfig-config >/dev/null
mkdir -p "$OUT"

# Never bundled: the C library family, the C++ runtime (the device's GPU driver needs its own),
# and anything that must match the device's GPU driver and compositor.
DENY='^(ld-linux|libc\.so|libm\.so|libdl\.so|libpthread\.so|librt\.so|libresolv\.so|libnss_|libutil\.so|libanl\.so|libBrokenLocale|libstdc\+\+|libgcc_s|libEGL|libGL|libGLES|libOpenGL|libgbm|libdrm|libwayland-|libudev|libvulkan|libmali)'

roots=(/usr/lib/aarch64-linux-gnu/libgtk-3.so.0 /usr/lib/aarch64-linux-gnu/libSDL2-2.0.so.0 /usr/lib/aarch64-linux-gnu/libfontconfig.so.1)
for f in "$APP"/*.so; do roots+=("$f"); done

declare -A seen
queue=("${roots[@]}")
while ((${#queue[@]})); do
  f="${queue[0]}"; queue=("${queue[@]:1}")
  while read -r path; do
    [[ -n "$path" && -e "$path" ]] || continue
    real=$(readlink -f "$path"); name=$(basename "$path")
    [[ -n "${seen[$name]:-}" ]] && continue
    seen[$name]=1
    [[ "$name" =~ $DENY ]] && continue
    [[ "$real" == "$APP"/* ]] && continue
    cp -L "$path" "$OUT/$name"
    queue+=("$real")
  done < <( { echo "$f"; ldd "$f" 2>/dev/null | awk '/=> \//{print $3}'; } )
done

# The two root libraries themselves are libraries too.
echo "bundled $(ls "$OUT" | wc -l) libraries, $(du -sh "$OUT" | cut -f1)"
echo "not bundled (expected from the device):"
for f in "${roots[@]}" "$OUT"/*; do ldd "$f" 2>/dev/null | awk '/=> \//{print $1}'; done | sort -u | grep -E "$DENY" | tr '\n' ' '
echo
echo "unresolved:"; for f in "$OUT"/*; do ldd "$f" 2>/dev/null | grep "not found" || true; done | sort -u
