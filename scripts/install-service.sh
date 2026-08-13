#!/usr/bin/env bash
# Register Island as a systemd --user service. Launch stays hidden;
# drag / island-ctl talk to the already-running process.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BIN="$ROOT/scripts/run-island.sh"
if [[ ! -x "$BIN" ]]; then
  echo "missing $BIN" >&2
  exit 1
fi
if [[ ! -x "$ROOT/Builds/Linux/Island" ]]; then
  echo "missing player: $ROOT/Builds/Linux/Island" >&2
  exit 1
fi

UNIT_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user"
UNIT="$UNIT_DIR/island.service"
mkdir -p "$UNIT_DIR"
cat > "$UNIT" <<EOF
[Unit]
Description=Island edge file service
After=graphical-session.target
PartOf=graphical-session.target

[Service]
Type=simple
ExecStart=$BIN
Restart=on-failure
RestartSec=2
Environment=ISLAND_DIRECT=1
Environment=ISLAND_BACKEND=x11
Environment=SDL_VIDEO_X11_NET_WM_BYPASS_COMPOSITOR=0

[Install]
WantedBy=graphical-session.target
EOF

systemctl --user daemon-reload
systemctl --user enable island.service
systemctl --user start island.service
echo "installed $UNIT"
echo "active=$(systemctl --user is-active island.service) enabled=$(systemctl --user is-enabled island.service)"
echo "stop:  systemctl --user stop island.service"
echo "quit:  $ROOT/scripts/island-ctl.sh quit"
