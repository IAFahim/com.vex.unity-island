#!/usr/bin/env bash
# Talk to one or more Island processes.
#   island-ctl.sh files /tmp/a.png /tmp/b.md
#   island-ctl.sh add /tmp/c.md
#   island-ctl.sh process
#   island-ctl.sh speak
#   island-ctl.sh stop
#   island-ctl.sh context
#   island-ctl.sh -p 17322 files /tmp/a.png
#   island-ctl.sh -i i17321 hide
#   island-ctl.sh --all hide
#   island-ctl.sh list
#   island-ctl.sh spawn
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PORT="${ISLAND_PORT:-17321}"
ID=""
ALL=0
DIR="${XDG_RUNTIME_DIR:-/tmp/island-$USER}/island"

while [[ $# -gt 0 && "$1" == -* ]]; do
  case "$1" in
    -p|--port) PORT="$2"; shift 2 ;;
    -i|--id) ID="$2"; shift 2 ;;
    -a|--all) ALL=1; shift ;;
    -h|--help)
      echo "usage: $0 [-p PORT|-i ID|--all] files|add <paths...> | process|speak|stop|context|note | show|hide|toggle|idle|quit | edge EDGE | span SPAN | list | spawn" >&2
      echo "  image paths open the photo bench (date / light / stamp). process writes copies." >&2
      exit 0 ;;
    *) break ;;
  esac
done

if [[ $# -lt 1 ]]; then
  echo "usage: $0 [-p PORT|-i ID|--all] files|add <paths...> | process|speak|stop|context|note | show|hide|toggle|idle|quit | edge EDGE | span SPAN | list | spawn" >&2
  exit 2
fi

alive() {
  local pid="$1"
  [[ -n "$pid" && -d "/proc/$pid" ]]
}

port_of_id() {
  local f="$DIR/$1"
  [[ -f "$f" ]] || return 1
  awk -F= '/^port=/{print $2}' "$f"
}

cmd=$(printf '%s' "$1" | tr '[:lower:]' '[:upper:]')
shift

if [[ "$cmd" == "LIST" ]]; then
  mkdir -p "$DIR"
  found=0
  for f in "$DIR"/*; do
    [[ -f "$f" ]] || continue
    pid=$(awk -F= '/^pid=/{print $2}' "$f")
    if ! alive "$pid"; then
      rm -f "$f"
      continue
    fi
    found=1
    tr '\n' ' ' < "$f"
    echo
  done
  [[ $found -eq 1 ]] || echo "(no islands)"
  exit 0
fi

if [[ "$cmd" == "SPAWN" ]]; then
  ISLAND_DIRECT=1 nohup "$ROOT/scripts/run-island.sh" spawn \
    >/tmp/island-spawn-$$.log 2>&1 &
  echo "spawned pid=$!"
  exit 0
fi

encode() {
  python3 -c 'import sys,urllib.parse; print("file://"+urllib.parse.quote(sys.argv[1]))' "$1"
}

case "$cmd" in
  FILES)
    toks=()
    for p in "$@"; do toks+=("$(encode "$p")"); done
    line="FILES ${toks[*]}"
    ;;
  ADD)
    toks=()
    for p in "$@"; do toks+=("$(encode "$p")"); done
    line="ADD ${toks[*]}"
    ;;
  SHOW|HIDE|TOGGLE|IDLE|QUIT|PROCESS|CONTEXT|NOTE|STOP) line="$cmd" ;;
  SPEAK) line="SPEAK $*" ;;
  EDGE|SPAN)
    [[ $# -ge 1 ]] || { echo "missing arg" >&2; exit 2; }
    line="$cmd $1" ;;
  *) echo "unknown $cmd" >&2; exit 2 ;;
esac

send() {
  local port="$1"
  if command -v nc >/dev/null; then
    printf '%s\n' "$line" | nc -q 1 127.0.0.1 "$port"
  else
    python3 - "$port" "$line" <<'PY'
import socket, sys
port=int(sys.argv[1]); line=sys.argv[2]
s=socket.create_connection(("127.0.0.1", port), 2)
s.sendall((line+"\n").encode())
print(s.recv(64).decode(), end="")
s.close()
PY
  fi
}

if [[ -n "$ID" ]]; then
  PORT=$(port_of_id "$ID") || { echo "no island $ID" >&2; exit 1; }
fi

if [[ $ALL -eq 1 ]]; then
  mkdir -p "$DIR"
  any=0
  for f in "$DIR"/*; do
    [[ -f "$f" ]] || continue
    pid=$(awk -F= '/^pid=/{print $2}' "$f")
    port=$(awk -F= '/^port=/{print $2}' "$f")
    if ! alive "$pid"; then
      rm -f "$f"
      continue
    fi
    echo -n "$port "
    send "$port"
    any=1
  done
  [[ $any -eq 1 ]] || { echo "no islands" >&2; exit 1; }
  exit 0
fi

send "$PORT"
