# Island

A small Unity standalone: 420×88 borderless pill that docks to a screen edge.

UI Toolkit only. No world camera. No URP.

## What it is

A **player**, not the Editor. `./scripts/run-island.sh` starts `Builds/Linux/Island`.

| | |
|---|---|
| Linux | `Vex.Island.Linux` + `libisland.so`. X11 chrome. Measured. |
| Windows | `Vex.Island.Windows` Win32 chrome. Player not built here. |
| macOS | `Vex.Island.OSX` stub (`Apply` false). |
| Native Wayland | Window exists; compositor owns position. Use XWayland to drag/dock. |

Idle on this machine: **~390 MB RSS / ~311 MB PSS, ~2.6% of one core, 22 MB VRAM**, 15 fps.

## Framework

Everything lives in `Packages/com.vex.island`. Assemblies do not share one dump:

| Assembly | What |
|---|---|
| `Vex.Island` | Core: layout, sense, offers, host, `IIslandChrome`. No UnityEngine. |
| `Vex.Island.Unity` | UITK app + `IslandWindow` facade |
| `Vex.Island.Linux` / `.Windows` / `.OSX` | One chrome each. `#if UNITY_STANDALONE_* && !UNITY_EDITOR` registers it |
| `Vex.Island.Editor` | Player settings + Linux/Windows/macOS build menu |

NuGet libs (Excel, XML, images, …) go through **NuGetForUnity** so we do not write parsers. Prefer `netstandard2.1` packages until Unity CoreCLR is the player runtime.

NuGet libs (Excel, XML, images, …) go through **NuGetForUnity** so we do not write parsers. Prefer `netstandard2.1` packages until Unity CoreCLR is the player runtime.

| | |
|---|---|
| Manager | **NuGet → Manage NuGet Packages** (UPM `com.github-glitchenzo.nugetforunity` **v4.5.0**) |
| Lock | `Packages/nuget-packages/packages.config` |
| Config | `Packages/nuget-packages/NuGet.config` (`InPackagesFolder`) |
| Restored DLLs | `Packages/nuget-packages/InstalledPackages/` (gitignored) |
| Restore | **NuGet → Restore Packages**, or `dotnet tool install -g NuGetForUnity.Cli && nugetforunity restore .` |

- **Edge** — top / bottom / left / right
- **Span** — monitor under the cursor, primary, or the whole virtual desktop (outer edge of all screens)
- **Offers** — `IIslandOffer` classifies by extension (image / sheet / xml / text / audio / video). First offer that accepts every file wins; mixed otherwise. Register more from any assembly.
- **Process** — `island-ctl.sh process` runs the offer hook (default `id:count`) and writes `note=` on the card.
- **Files** — one island holds many paths (`FILES` replace, `ADD` append)
- **Instances** — one process = one pill. Ports `17321`–`17328`. Cards in `$XDG_RUNTIME_DIR/island/`
- **IPC** — `127.0.0.1:<port>` (`island-ctl.sh -p` / `-i` / `--all` / `list` / `spawn` / `process` / `context`)

Launch only starts the service (no pill). Drag a file and the pill snaps to the outer edge with the name. Drop, release, or Esc hides it.

```bash
./scripts/install-service.sh    # systemd --user, stays across logins
./scripts/run-island.sh         # start (or activate the unit)
./scripts/island-ctl.sh quit    # stop the player
```

```bash
./scripts/run-island.sh
# or poke it yourself:
./scripts/island-ctl.sh files ~/shot.png
./scripts/island-ctl.sh files ~/data.xlsx
./scripts/island-ctl.sh files ~/doc.xml
./scripts/island-ctl.sh process
./scripts/island-ctl.sh spawn          # another pill
./scripts/island-ctl.sh list
./scripts/island-ctl.sh --all hide
```

Drag-start is an XFixes event in the player (`XdndSelection`). `scripts/island-watch.py` is optional AT-SPI, not started by the player.

## Build

```bash
make -C Native
# default Linux. ISLAND_BUILD_TARGET=win|osx for the others.
unity build . --target StandaloneLinux64 --execute-method Vex.Island.Editor.IslandBuilder.SetupAndBuild --allow-dirty-build --no-tail
```

Esc hides. `island-ctl.sh quit` stops the player. Drag the pill (XWayland).
