# Island

A small Unity standalone: 420×88 borderless pill that docks to a screen edge.

UI Toolkit only. No world camera. No URP.

## What it is

A **player**, not the Editor. `./scripts/run-island.sh` starts `Builds/Linux/Island`.

| | |
|---|---|
| Linux | X11 plugin: borderless, topmost, rounded shape, root-pointer drag. Measured. |
| Windows | Win32 P/Invoke in `IslandWindow.cs`. Not built here. |
| macOS | Not written. |
| Native Wayland | Window exists; compositor owns position. Use XWayland to drag/dock. |

Idle on this machine: **~390 MB RSS / ~311 MB PSS, ~2.6% of one core, 22 MB VRAM**, 15 fps.

## Framework

`IslandHost` is the future hook. UITK only paints.

- **Edge** — top / bottom / left / right
- **Span** — monitor under the cursor, primary, or the whole virtual desktop (outer edge of all screens)
- **Mode** — idle clock, or “N files” when something selected files
- **IPC** — `127.0.0.1:17321` (works on Windows later too)

Launch only starts the service (no pill). Drag a file and the pill snaps to the outer edge with the name. Drop, release, or Esc hides it.

```bash
./scripts/install-service.sh    # systemd --user, stays across logins
./scripts/run-island.sh         # start (or activate the unit)
./scripts/island-ctl.sh quit    # stop the player
```

```bash
./scripts/run-island.sh
# or poke it yourself:
./scripts/island-ctl.sh files ~/Pictures/a.png
./scripts/island-ctl.sh hide
```

Drag-start is an XFixes event in the player (`XdndSelection`). `scripts/island-watch.py` is optional AT-SPI, not started by the player.

## Build

```bash
make -C Native
unity run . --timeout 400 -- -executeMethod Vex.Island.Editor.IslandBuilder.SetupAndBuild -quit
```

Esc hides. `island-ctl.sh quit` stops the player. Drag the pill (XWayland).
