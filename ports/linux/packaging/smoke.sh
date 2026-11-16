#!/usr/bin/env bash
# Boots the built Linux head under a virtual X display and checks it survives and logs no errors.
# Usage: smoke.sh [seconds]   (default 25). Needs xvfb-run, xwd and ImageMagick `convert`.
# Env: PKFORGE_PAD_SCRIPT="Down,A"  buttons sent to the app 3 s after start (see LinuxHosting).
#      SMOKE_OUT=<dir>              where smoke.png goes (default: the temp folder).
#      SMOKE_RENDERER=raster        force OpenMaui's CPU renderer (use when GPU init fails under Xvfb).
set -u
SECONDS_TO_WAIT="${1:-25}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
BASE="$ROOT/ports/linux/PKForge.Linux/bin/Debug/net10.0"
# The apphost lands in the base folder, or in a runtime-id subfolder when built with -r.
BIN=""
for d in "$BASE" "$BASE"/linux-arm64 "$BASE"/linux-x64; do
  [ -x "$d/PKForge" ] && { BIN="$d"; break; }
done
[ -n "$BIN" ] || { echo "smoke: built PKForge not found under $BASE" >&2; exit 2; }

TMP="$(mktemp -d)"
OUT="${SMOKE_OUT:-$TMP}"
mkdir -p "$OUT"; OUT="$(cd "$OUT" && pwd)"
export XDG_DATA_HOME="$TMP/data" XDG_CACHE_HOME="$TMP/cache" XDG_CONFIG_HOME="$TMP/config"
mkdir -p "$XDG_DATA_HOME" "$XDG_CACHE_HOME" "$XDG_CONFIG_HOME" "$OUT"
export MAUI_PREFER_X11=1
[ -n "${SMOKE_RENDERER:-}" ] && export OPENMAUI_RENDERER="$SMOKE_RENDERER"

# Runs inside the virtual display: start the app, wait, screenshot, report whether it is alive.
cat > "$TMP/inner.sh" <<INNER
#!/usr/bin/env bash
cd "$BIN"
./PKForge > "$TMP/app.out" 2>&1 &
APP=\$!
sleep $SECONDS_TO_WAIT
if kill -0 \$APP 2>/dev/null; then echo alive > "$TMP/status"; else echo exited > "$TMP/status"; fi
xwd -root -silent | convert xwd:- "$OUT/smoke.png"
kill \$APP 2>/dev/null; wait \$APP 2>/dev/null
INNER
chmod +x "$TMP/inner.sh"
xvfb-run -a -s "-screen 0 1024x768x24" "$TMP/inner.sh"

fail=0
[ "$(cat "$TMP/status" 2>/dev/null)" = alive ] || { echo "smoke: FAIL process exited early"; fail=1; }
LOGS="$(find "$TMP" -name pkforge.log 2>/dev/null)"
if [ -z "$LOGS" ]; then echo "smoke: FAIL no pkforge.log written"; fail=1
elif grep -h " ERROR " $LOGS; then echo "smoke: FAIL log contains ERROR lines"; fail=1; fi
CRASH="$(find "$TMP" -name 'crash-*.txt' 2>/dev/null)"
[ -s "$OUT/smoke.png" ] || { echo "smoke: FAIL no screenshot"; fail=1; }
[ -z "$CRASH" ] || { echo "smoke: FAIL crash reports: $CRASH"; fail=1; }
[ $fail = 0 ] || { echo "--- app output tail"; tail -40 "$TMP/app.out"; }
echo "smoke: screenshot $OUT/smoke.png  (temp $TMP)"
[ $fail = 0 ] && echo "smoke: OK"
exit $fail
