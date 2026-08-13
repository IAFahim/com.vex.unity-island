#!/usr/bin/env bash
# Start the Island service (hidden). Does not show the pill.
# If the user unit is installed, this just starts systemd --user island.service.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BIN="$ROOT/Builds/Linux/Island"

SPAWN=0
if [[ "${1:-}" == "spawn" || "${1:-}" == "extra" ]]; then
  SPAWN=1
  shift
  export ISLAND_DIRECT=1
fi

if [[ $SPAWN -eq 0 && "${ISLAND_DIRECT:-0}" != 1 ]] && command -v systemctl >/dev/null &&
   systemctl --user cat island.service >/dev/null 2>&1; then
  systemctl --user start island.service
  echo "island.service $(systemctl --user is-active island.service)"
  exit 0
fi

if [[ ! -x "$BIN" ]]; then
  echo "missing player: $BIN (build with Island/Build Linux Player)" >&2
  exit 1
fi

export SDL_VIDEO_X11_NET_WM_BYPASS_COMPOSITOR=0

# Native Wayland cannot set position (xdg-shell). XWayland can, so drag works.
BACKEND="${ISLAND_BACKEND:-x11}"
ARGS=(-popupwindow -screen-fullscreen 0 -screen-width 88 -screen-height 420)

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
