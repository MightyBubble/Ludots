# Scenario Card: navigation2d-spatial-adaptive-rebuild

## Intent
- Player goal: keep a 10k cross-traffic crowd responsive without hidden runtime heuristics.
- Gameplay domain: `Navigation2DSteeringSystem2D` local avoidance with steady-state SoA sync and adaptive spatial rebuild.

## Determinism Inputs
- Seed: none
- Map: empty 2D plane, `100 x 100` lattice, grid cell size `100cm`
- Clock profile: fixed `1/60s`
- Initial entities: `10,000` `NavAgent2D` entities with identical `NavKinematics2D`
- Crowd pattern: `QuarterCrossCellMigration` with `25%` rows crossing cell boundaries each measured tick
- Config source: `game.json -> ConfigPipeline.MergeGameConfig() -> GameConfig.Navigation2D -> Navigation2DRuntime`
- Playable mod source: `mods/Navigation2DPlaygroundMod/assets/game.json`

## Action Script
1. Merge explicit `Navigation2D.Spatial` config through the shared config pipeline.
2. Run non-benchmark `Navigation2DTests` regression suite.
3. Run `10k` `StaticCrowd` ORCA smoke benchmark.
4. Run `10k` `QuarterCrossCellMigration` benchmark for `ORCA`, `Hybrid`, and `Sonar`.
5. Build `Ludots.App.Raylib` Release and keep `Navigation2DPlaygroundMod` as the playable acceptance entry.

## Expected Outcomes
- Primary success condition: heavy cell migration no longer causes triple-digit millisecond hitches.
- Failure branch condition: adaptive rebuild is bypassed, tombstones accumulate, and neighbor probes collapse again.
- Key metrics: steering ms/tick, cell-map update ms/tick, dirty agents, cell migrations, allocations, playable mod build status.

## Evidence Artifacts
- `artifacts/acceptance/navigation2d-spatial-adaptive-rebuild/trace.jsonl`
- `artifacts/acceptance/navigation2d-spatial-adaptive-rebuild/battle-report.md`
- `artifacts/acceptance/navigation2d-spatial-adaptive-rebuild/path.mmd`

## Header
- scenario: `navigation2d-spatial-adaptive-rebuild`
- build: `dotnet build src/Apps/Raylib/Ludots.App.Raylib/Ludots.App.Raylib.csproj -c Release /nodeReuse:false`
- execution date: `2026-03-07`
- benchmark project: `src/Tests/Navigation2DTests/Navigation2DTests.csproj`
- playable entry: `dotnet run --project src/Apps/Raylib/Ludots.App.Raylib/Ludots.App.Raylib.csproj -c Release -- game.navigation2d.json`

## Timeline
- `[T+000] ConfigPipeline.MergeGameConfig -> GameConfig.Navigation2D | Loaded explicit Spatial subtree | UpdateMode=Adaptive | Thresholds=128/1024 | Playground default=5000 agents per team`
- `[T+001] Regression suite -> Navigation2DTests non-benchmark | Pass | 39/39 tests green | duration 19s`
- `[T+002] Benchmark StaticCrowd / ORCA -> 10,000 agents | Median 9.5508ms/tick | Alloc 0B`
- `[T+003] Benchmark QuarterCrossCellMigration / ORCA -> 10,000 agents | 11.7765ms/tick | CellMap 0.2523ms | Dirty 2500.0 | Migrations 416.7`
- `[T+004] Benchmark QuarterCrossCellMigration / Hybrid -> 10,000 agents | 10.7081ms/tick | CellMap 0.2493ms | Dirty 2500.0 | Migrations 416.7`
- `[T+005] Benchmark QuarterCrossCellMigration / Sonar -> 10,000 agents | 11.2656ms/tick | CellMap 0.2212ms | Dirty 2500.1 | Migrations 416.7`
- `[T+006] Playable mod wiring -> Navigation2DPlaygroundMod | Config-driven 5k/team start | HUD shows spatial mode, rebuild count, dirty total, migration total`
- `[T+007] Release build -> Ludots.App.Raylib | Success | 0 errors`
- `[T+008] Outcome -> PASS | Adaptive rebuild removes churn-induced probe collapse in 10k migration scenario`

## Outcome
- success: yes
- verdict: the spatial hot path now follows the ProjectDawn-style rebuild direction instead of relying on tombstone-heavy incremental mutation during churn.
- reason: the exact `QuarterCrossCellMigration` case that previously exploded now stays around `10-12ms/tick`, and `Nav2DCellMap.UpdatePositions` stays around `0.22-0.25ms`.
- comparison note: earlier investigation on this branch recorded the same ORCA scenario at roughly `434.9739ms/tick` before adaptive rebuild was introduced.

## Summary Stats
- non-benchmark regression tests: `39` passed
- static ORCA steering tick: `9.5508ms`
- quarter-cross ORCA steering tick: `11.7765ms`
- quarter-cross Hybrid steering tick: `10.7081ms`
- quarter-cross Sonar steering tick: `11.2656ms`
- quarter-cross ORCA cell-map update: `0.2523ms`
- quarter-cross Hybrid cell-map update: `0.2493ms`
- quarter-cross Sonar cell-map update: `0.2212ms`
- dirty agents per measured tick: `2500`
- cell migrations per measured tick: `416.7`
- allocated bytes on measured thread: `0`
- playable acceptance start crowd: `5000/team`, `10000 total`
- conclusion: the large hitch was not ORCA math itself; it was the degraded read-side probe cost after repeated cell deletions. Adaptive rebuild removes that failure mode.
