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

Starts **hidden at 1 fps**. Drag a selected file in Files/Nautilus and the pill slides out with the name. Drop or release and it hides again. Esc still quits.

```bash
./scripts/run-island.sh
# or poke it yourself:
./scripts/island-ctl.sh files ~/Pictures/a.png
./scripts/island-ctl.sh hide
```

`scripts/island-watch.py` (started by the player) watches X11 button-drag + AT-SPI selection in Nautilus/Files. Native Wayland file managers work if accessibility is on.

## Build

```bash
make -C Native
unity run . --timeout 400 -- -executeMethod Vex.Island.Editor.IslandBuilder.SetupAndBuild -quit
```

Esc quits. Drag the pill (XWayland).
