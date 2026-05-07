# Mass Nav Web Parity Playground

`MassNavWebParityMod` is the current SSOT playground for the high-performance mass navigation path. It is a playable Raylib showcase, not a production `Navigation2D` replacement yet.

The design goal is simple: from a designer or player point of view this is a standard RTS map, even when the battlefield is 64km x 64km. The user should not reason about implementation terms such as hot zones or solver pockets. They look somewhere with the camera, select units, and right-click any world point. The runtime then derives the internal flow/crowd working set from two player-visible signals:

- what the camera is currently observing
- where the latest selected group or team command wants gameplay to happen

## Existing Infrastructure Reused

- Launcher graph and `ConfigPipeline`: the mod is launched through `scripts/run-mod-launcher.cmd`, not by a private bootstrap.
- Ludots UI runtime: panels and HUD use the existing UI/presentation services. Missing UI services are a launch/evidence error.
- Formal selection: selection truth is `SelectionRuntime` / `SelectionSetKeys.LivePrimary`.
- Formal orders: movement commands go through GAS `OrderBufferSystem` and order type `massNavMove`.
- Minimap capability: world clicks and debug snapshots use `MinimapControlMod.Runtime`.
- Camera services: camera jumps use the same camera request path as the playable Raylib showcase.
- World streaming/spatial state: `WorldGridLoadedChunks` and `SpatialQueryService.SetLoadedChunks(...)` are reused for the active working set.

No fallback path is valid for this playground. Missing minimap, selection, board, team lookup, presenter, order type, or world config must fail loudly.

## Player-Facing Contract

- The minimap represents the full configured world.
- The minimap shows the current camera rectangle.
- Clicking any minimap point moves the camera to that exact world coordinate.
- Box-selected units can right-click move to any valid world coordinate.
- Empty parts of the world are still valid world space; they show grid and coordinates instead of a black or invalid screen.
- Known contacts are landmarks only. They are not playable boundaries and are not solver ownership.

## Runtime Contract

- `MassNavWorldConfig.WorldWidthCm` and `WorldHeightCm` define the 64km board-scale world.
- `SolverWindowWidthCm` and `SolverWindowHeightCm` define the current SoA solver cache size. Today it must match `MassNavWebParitySimState.FieldWidthCm` and `FieldHeightCm`.
- `MassNavSimulationRuntime` owns the runtime solver window center. It must not mutate configured known contacts.
- Camera focus calls `ObserveCameraFocus(...)`; command targets call `FocusCommandTarget(...)`.
- `FlowWorkArea` is the requested gameplay working set. It is built from the current camera rectangle, the latest command target, and the selected units that produced the command.
- The current `SolverWindow` is the fixed-size SoA execution cache. It is allowed to be smaller than `FlowWorkArea`; this is the known single-window implementation limit, not a player-facing rule.
- `WorldGridLoadedChunks` and `SpatialQueryService.SetLoadedChunks(...)` are reused for the streamed world working set.
- Missing minimap, selection, board, team lookup, presenter, or world config is an error. There is no hidden bootstrap path.

## Current Frame Flow

1. Ludots input and selection systems update the official selection runtime.
2. `MassNavSelectionSyncSystem` copies the current selection into the mass-nav runtime.
3. `MassNavCommandBridgeSystem` reads right-click ground commands through the official order/input path.
4. A command target updates `FlowWorkArea`, records command focus, and holds that focus for a configured number of ticks.
5. `MassNavFormationSystem` observes the real camera rectangle every sim tick through Ludots camera utilities. Camera focus expands or moves `FlowWorkArea`; the solver cache only moves when the camera leaves the configured safe margin or a command target is active.
6. `MassNavGroupRuntime` updates group and formation slots.
7. `MassNavWebParitySimState` advances SoA positions, velocities, flow sampling, separation, and hard resolve.
8. Entity `WorldPositionCm` and `VisualTransform` are synchronized back for Ludots presentation and selection.
9. Primitive presentation draws the agents, solver window boundary, world grid, and minimap/HUD diagnostics.

## Current Large-World Model

This version is a moving single-window solver:

- World coordinates are authoritative.
- The SoA grid is a high-performance local solver cache.
- Camera focus, selected-unit bounds, and command targets dynamically request a `FlowWorkArea`.
- The solver cache currently chooses one focus inside that work area: command target during command hold, otherwise camera/work-area focus.
- Agents outside the local solver field are not clamped into the edge; they remain in world space and are skipped by local hash/obstacle work until the window reaches them.

This is enough to validate the intended UX and the 64km RTS interaction contract. It is not yet the final multi-window or multi-resolution flow allocator. If production needs several distant battles simulated at once, the next step is to generalize this into multiple explicit solver windows, not to expose implementation regions to players.

## Config

Main config: `mods/showcases/navigation/MassNavWebParityMod/assets/MassNavWebParityConfig.json`

Important fields:

- `world.worldWidthCm` / `world.worldHeightCm`: full battlefield size.
- `world.solverWindowWidthCm` / `world.solverWindowHeightCm`: SoA solver cache size.
- `world.streamingChunkSizeCm`: chunk size for the reused loaded-chunks service.
- `world.streamingRadiusCm`: streamed chunk radius around camera/command focus.
- `world.cameraFocusShiftThresholdCm`: safe inner margin before camera motion shifts the solver window.
- `world.commandFocusHoldTicks`: ticks to keep the command target as solver focus before camera focus can take over again.
- `world.workAreaPaddingCm`: padding applied around camera, selected units, and command target when building `FlowWorkArea`.
- `world.workAreaMaxWidthCm` / `world.workAreaMaxHeightCm`: maximum requested work-area dimensions. This caps diagnostics and streaming demand while the current solver cache remains fixed-size.
- `world.hotZones`: currently named legacy config field for known contacts. Treat these as landmarks, not gameplay boundaries.

## UAT Checklist

Manual Raylib UAT:

- Launch `mass_nav_web_parity_raylib`.
- Verify the minimap shows the full 64km battlefield and the `CAM` rectangle.
- Click the minimap center, then a far corner, then an empty area. The camera should move to each coordinate and never black out.
- Use the contact buttons only as camera shortcuts. The contact marker itself must not move.
- Box-select units and right-click a far world coordinate. Units should receive a move order. The HUD/panel should report `FlowWorkArea` driven by `selection command`, including selected-unit bounds and the command target.
- Confirm the blue 3D rectangle is the current solver cache and the green 3D rectangle is the larger requested flow work area. Neither rectangle is a player movement boundary.
- Pan the camera away from the command target. The solver window should not instantly snap back until `commandFocusHoldTicks` expires.
- After the hold expires, continue moving the camera; the dynamic flow window should follow only after crossing the safe threshold.
- Check the HUD and left panel: they should show real render FPS, `FlowWorkArea` center/size/reason, solver cache center/size, chunk count, command rejects, and per-stage timings.
- Reset scene. Agent count, group state, selection mirror, and runtime diagnostics should return to a clean state.

Automated evidence UAT:

```powershell
.\scripts\run-mod-launcher.cmd cli launch mass_nav_web_parity --adapter raylib --record artifacts\acceptance\mass-nav-web-parity-large-world-rts
```

This recorder runs the real launcher graph and writes:

- `artifacts/acceptance/mass-nav-web-parity-large-world-rts/battle-report.md`
- `artifacts/acceptance/mass-nav-web-parity-large-world-rts/trace.jsonl`
- `artifacts/acceptance/mass-nav-web-parity-large-world-rts/path.mmd`
- `artifacts/acceptance/mass-nav-web-parity-large-world-rts/summary.json`
- `artifacts/acceptance/mass-nav-web-parity-large-world-rts/visible-checklist.md`
- `artifacts/acceptance/mass-nav-web-parity-large-world-rts/screens/timeline.png`

The automated UAT must pass all of these player-facing checks:

- 64km world dimensions are active.
- At least four dynamic teams are present.
- The minimap starts as the full world.
- Camera jumps land on all configured test points, including all four near-edge corners and empty coordinates.
- Formal selection is populated through `SelectionRuntime`.
- A GAS `massNavMove` command creates active orders/groups and moves the selected units.
- Reset clears selection and group state.
- A second command after reset moves the selected units again.
- An empty in-bounds world coordinate accepts a normal move command.
- Every dynamic team can be selected and commanded independently through the same formal path.
- Near-edge in-bounds world coordinates accept normal camera jumps and move commands on all four corners and all four side midpoints.
- Every out-of-world boundary probe, including just-over-edge cases, is rejected and counted; commands are not clamped or silently redirected.
- No agent, solver window, or flow work-area may leave the configured world bounds during camera jumps, movement, reset, legal edge commands, invalid boundary probes, or soak.
- The long soak keeps streaming chunks active.
- Memory evidence records both total managed-watermark growth and a steady-state probe. Total growth is allowed to reflect first-use capacity expansion only if the final steady-state probe stays bounded.

The recorder is headless evidence. It records simulation tick cost, solver buckets, memory watermarks, and screenshots. It does not record real render FPS; use the live Raylib HUD or a dedicated renderer benchmark for FPS.

Repeatable soak UAT:

```powershell
.\scripts\acceptance\run-mass-nav-web-parity-large-world-uat.ps1 -Iterations 3 -OutputRoot artifacts\acceptance\mass-nav-web-parity-large-world-soak
```

Overnight soak UAT:

```powershell
.\scripts\acceptance\run-mass-nav-web-parity-large-world-uat.ps1 -Iterations 0 -UntilLocalTime 06:00 -OutputRoot artifacts\acceptance\mass-nav-web-parity-large-world-overnight -StopOnFailure
```

The soak runner repeats the same launcher evidence contract and writes:

- `soak-report.md`: designer-readable pass/fail summary, UAT matrix, links to each run timeline.
- `soak-summary.jsonl`: one machine-readable row per run.
- `run-000N/battle-report.md`: detailed scenario card for that run.
- `run-000N/trace.jsonl`: per-snapshot diagnostics.
- `run-000N/screens/timeline.png`: visual proof strip.

Soak acceptance is not a different standard. It is the same Ludots evidence path repeated until a configured iteration count or local deadline. Missing `summary.json`, missing screenshots, missing selection/order/minimap services, or launcher failure is a failed run, not a fallback success.

Enhanced boundary coverage currently records:

- `multi_team_command_advance_cm`: one movement proof per dynamic team.
- `edge_inside_command_advance_cm`: eight legal near-edge movement proofs, covering four corners and four side midpoints.
- `agents_outside_world`: must stay `0` on every trace row.
- `multi_team_min_advance_cm` and `edge_inside_min_advance_cm`: condensed soak columns for quick UAT scanning.

Current acceptance artifact signature:

```text
mass_nav_web_parity_large_world_rts_uat|agents:10000|teams:4|firstAdvance:4739|secondAdvance:10598|emptyAdvance:2234|multiTeamMin:2293|edgeMin:867|workRev:69|rejects:9|boundaryRejects:8|chunks:36
```

Current evidence highlights:

- `10,000` agents across `4` teams.
- First command advance: about `47m`.
- Second command advance after reset: about `106m`.
- Empty-world command advance: about `22m`.
- Multi-team command minimum advance: about `23m`.
- Legal near-edge command minimum advance: about `9m`.
- Invalid command rejects: `9`, including all eight boundary probes.
- Steady managed growth over the final `240` ticks: about `0.55MB`.
- Steady allocated bytes over the final `240` ticks: about `2.74MB`.
- Warm median headless tick remains headless evidence only; live render FPS must be checked in the Raylib HUD.

## Known Limits

- The current solver cache size is fixed to the SoA grid constants. The config is explicit so this constraint is visible and fail-fast.
- Flow allocation is dynamic by focus position, not yet dynamic by multiple simultaneous hotspots.
- Known contacts still use legacy `HotZone` type names internally. They are not user-facing hot zones and should be renamed to `KnownContact` in a cleanup pass.
- The crowd simulation still overlaps conceptually with production `Navigation2D`; this playground proves the high-performance strategy before deeper mainline convergence.
- The automated evidence run still shows a roughly `97MB` managed memory watermark increase after warm baseline when it first exercises far commands and screenshots. The final steady-state probe is bounded, so this is tracked as capacity-watermark evidence rather than a confirmed leak. If the steady-state probe grows beyond the documented thresholds, the UAT must fail.

## Read Order

1. `mods/showcases/navigation/MassNavWebParityMod/Runtime/MassNavPlaygroundRuntime.cs`
2. `mods/showcases/navigation/MassNavWebParityMod/Runtime/MassNavSimulationRuntime.cs`
3. `mods/showcases/navigation/MassNavWebParityMod/Runtime/MassNavWebParitySimState.cs`
4. `mods/showcases/navigation/MassNavWebParityMod/Systems/MassNavCommandBridgeSystem.cs`
5. `mods/showcases/navigation/MassNavWebParityMod/Systems/MassNavFormationSystem.cs`
6. `mods/showcases/navigation/MassNavWebParityMod/UI/MassNavPlaygroundPanelController.cs`
7. `mods/capabilities/minimap/MinimapControlMod/Runtime/MinimapControlRuntime.cs`
