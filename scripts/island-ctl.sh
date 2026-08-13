#!/usr/bin/env bash
# Talk to a running Island player (127.0.0.1:17321).
#   island-ctl.sh files /tmp/a.png /tmp/b.md
#   island-ctl.sh hide | show | toggle | idle | quit
#   island-ctl.sh edge top|bottom|left|right
#   island-ctl.sh span active|primary|virtual
set -euo pipefail
PORT=17321
if [[ $# -lt 1 ]]; then
  echo "usage: $0 files <paths...> | show | hide | toggle | idle | quit | edge EDGE | span SPAN" >&2
  exit 2
fi
cmd=$(printf '%s' "$1" | tr '[:lower:]' '[:upper:]')
shift
case "$cmd" in
  FILES) line="FILES $*" ;;
  SHOW|HIDE|TOGGLE|IDLE|QUIT) line="$cmd" ;;
  EDGE|SPAN)
    [[ $# -ge 1 ]] || { echo "missing arg" >&2; exit 2; }
    line="$cmd $1" ;;
  *) echo "unknown $cmd" >&2; exit 2 ;;
esac
if command -v nc >/dev/null; then
  printf '%s\n' "$line" | nc -q 1 127.0.0.1 "$PORT"
else
  python3 - "$PORT" "$line" <<'PY'
import socket, sys
port=int(sys.argv[1]); line=sys.argv[2]
s=socket.create_connection(("127.0.0.1", port), 2)
s.sendall((line+"\n").encode())
print(s.recv(64).decode(), end="")
s.close()
PY
fi
