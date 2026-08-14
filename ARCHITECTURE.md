# Island

A borderless edge capsule. One process, one pill. Features are offers. The player is a reducer: event in, frame out, effects at the rim.

## Axioms

1. **Data is not behavior.** A frame is values. A step is a total function `Frame × input → Frame`. Chrome, speak, ffmpeg, UITK are effects applied *after* the step.
2. **Immutable by default.** `IslandFrame` is a readonly struct. Steps return a new frame. The host holds one current frame.
3. **Total.** Empty files, unknown extensions, missing EXIF, failed speak — every path returns a frame. No thrown control flow in Core.
4. **One feature slot.** `IIslandOffer` is the only way to add work. First offer that accepts every file wins. Mixed files are mixed. There is no `if (photo)` in `Update`.
5. **Pose is independent of work.** Edge and slide Y survive dismiss. Work (files, note, line, bench) does not.
6. **Shows = visible ∧ (bench ∨ files).** Holds (speak, photo) means a finished drag does not dismiss. OpensBench means Hold also opens the bench. ActsOnDrop means Hold also runs `Process`.
7. **One window, changing shape.** The player covers the current monitor. Closed = pill-only XShape (click-through desk). Open = full-monitor takeover scrim + capsule. Click the dim or Esc retreats (bench, then hide). Never a second window. Same pose/shape/map → no X call.
8. **Deletion shrinks.** Removing an offer removes a feature. Removing a field from the frame is a compile error. No dual paths.
9. **No comments in Core.** Names are the spec. This file is the only prose.

## Assemblies

| Assembly | Contents |
|---|---|
| `Vex.Island` | Frame, kernel, layout, shape, sense, offers, host, chrome interface. No UnityEngine. |
| `Vex.Island.Unity` | UITK paint, `IslandWindow` (idempotent chrome facade). |
| `Vex.Island.Linux` / `.Windows` / `.OSX` | One `IIslandChrome`. Registers itself. |
| `Vex.Island.Editor` | Build menu. |

## Frame

```
files[]  context  offerId  holds  opensBench  actsOnDrop
edge  slideY  span
visible  bench
line  note
```

`IslandKernel.Hold(prev, paths)` keeps only photos when any image is in the drop, then classifies via `IslandOffers.Resolve`. The bench follows `context.Kind`: speak / photo / files. Clicking the pill never opens speak settings for mixed files.  
`IslandKernel.Dismiss(prev)` keeps pose, drops work.  
`IslandKernel.Pose(prev, edge, y)` / `Bench(prev, open)` / `Speak(prev, line, note)` / `Note(prev, note, line)`.

## Effects (not in the kernel)

| Effect | Owner |
|---|---|
| Dock, XShape, map | `IslandWindow` + platform chrome |
| Read text aloud | `IslandSpeak` (ReadAloud / spd-say) |
| Stamp + export copies | `IslandPhoto` (ffmpeg, PhotoLog settings) |
| Paint | `IslandBench` / `IslandApp` |
| Quiet fade then dismiss | `IslandApp` via `IslandQuiet` |

## Adding a feature

1. Implement `IIslandOffer` (`Id`, `Kind`, `Accepts`, `Process`, `Holds`, `OpensBench`, `ActsOnDrop`).
2. `IslandOffers.Register` it.
3. If it needs a bench pane, paint when `context.Kind` matches. Do not add a mode to `Update`.
4. Process must be free of Unity objects (IPC thread may call it).

## Tests

Kernel tests must fail if Hold/Dismiss/Pose lose pose, if empty input is not Idle, or if two Holds of the same paths are not equal. Layout and shape stay pure.
