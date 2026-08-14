# Island

Read `ARCHITECTURE.md` first. Those axioms are the spec.

- New work is an `IIslandOffer`. Do not add a branch to `IslandApp.Update`.
- Core is pure: `IslandKernel` returns a new `IslandFrame`. Effects (chrome, speak, ffmpeg, UITK) run after the step.
- No comments in Core. Names carry the contract.
- Pose (edge, slide Y) survives dismiss. Work does not.
- Chrome calls are idempotent. Shape before map.
- Tests must fail if Hold/Dismiss/Pose break those equalities.
