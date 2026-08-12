#!/usr/bin/env bash
# Prefer native Wayland. Unity still speaks XWayland if you pass -force-x11.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BIN="$ROOT/Builds/Linux/Island"
if [[ ! -x "$BIN" ]]; then
  echo "missing player: $BIN (build with Island/Build Linux Player)" >&2
  exit 1
fi

export SDL_VIDEO_X11_NET_WM_BYPASS_COMPOSITOR=0

# Native Wayland cannot set position (xdg-shell). XWayland can, so drag works.
BACKEND="${ISLAND_BACKEND:-x11}"
ARGS=(-popupwindow -screen-fullscreen 0 -screen-width 420 -screen-height 88)

if [[ "$BACKEND" == "wayland" && -n "${WAYLAND_DISPLAY:-}" ]]; then
  ARGS+=(-force-wayland)
  echo "backend=wayland"
elif [[ "${ISLAND_ARGB:-0}" == "1" ]]; then
  VIS="$(python3 - <<'PY'
import re, subprocess, sys
try:
    out = subprocess.check_output(["xdpyinfo"], text=True, errors="replace")
except Exception:
    sys.exit(0)
vid = None
for line in out.splitlines():
    m = re.search(r"visual id:\s*(0x[0-9a-fA-F]+|\d+)", line)
    if m:
        vid = m.group(1)
        continue
    if vid and re.search(r"depth:\s*32\b", line):
        print(vid)
        break
PY
)"
  if [[ -n "${VIS:-}" ]]; then
    export SDL_VIDEO_X11_VISUALID="$VIS"
    echo "ARGB visual $VIS"
  fi
  echo "backend=x11"
else
  echo "backend=x11"
fi

exec "$BIN" "${ARGS[@]}" "$@"
