# Capability Standard Physics2D Playground v2 Acceptance

| Check | Evidence |
| --- | --- |
| Startup boundary | `physics2D.enabled=true`, `navigation2D.enabled=true`, production `Navigation2DRuntime` present |
| Runtime boundary | v2 installs interaction only; production `Physics2DSimulationSystem` count remains 1 |
| Spawn path | `ConfigPipeline` catalog -> map focus event -> `RuntimeEntitySpawnQueue.EnqueueMany` -> `RuntimeEntitySpawnSystem` for 5 first-screen entities |
| Launch visibility | every first-screen entity has direct performer bootstrap; default camera frames the playground instead of the global debug grid |
| Physics-only mode | physics body has `Position2D/Velocity2D/Mass2D`, no `OrderBuffer`, no Nav components |
| CC/knockback regression | displacement under `MovementSuppressed2D` clears velocity and lands at the configured displacement distance |
| Nav mode | nav final X `-540` cm, desired X `360` cm/s, obstacle nav `True` physics `True` |
| v1 benchmark carryover | LeftShift+Q/W/E/R/T/Y/U/O/P count slots, right-click/runtime burst spawn, and C GAS force pulse retained; benchmark bodies `220`, sample Vx `90.826` cm/s |
| Playground tools | HUD exposes FPS/frame/entity stats; G static polygons `1`, F friction zones `3`, X explosion affected `40` bodies |
| Explosion spatial benchmark | local AoE used `SpatialQueries.QueryRadius` candidates `43` dropped `0` while far benchmark bodies remained outside ForceInput |
| Friction zone benchmark design | zones are static `PhysicsMaterial2D` box colliders; material friction is exercised by production Physics2D broadphase/contact solver instead of a parallel area-field system |
| Physics stats | Hz `15`, potential pairs `210`, contact pairs `209`, last update `2.0712` ms |
| Test tick timings | frames `82`, avg `1.1523` ms, max `20.5529` ms |

## Keyframes

| Frame | Mode | Physics X | Physics Vx | Suppressed | Physics Has Nav | Nav X | Nav Vx | Nav Desired X | Nav Obstacle | Physics Obstacle | Benchmark Bodies | Benchmark Vx | Static Polygons | Friction Zones | Explosion Last | Explosion Candidates | Explosion Dropped |
| ---: | --- | ---: | ---: | :---: | :---: | ---: | ---: | ---: | :---: | :---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 0 | PhysicsOnly | -712 | 119.76 | False | False | -900 | 0 | 0 | True | True | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| 7 | PhysicsOnly | -712 | 539.76 | False | False | -900 | 0 | 0 | True | True | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| 7 | PhysicsOnly | -712 | 0 | True | False | -900 | 0 | 0 | True | True | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| 18 | PhysicsOnly | -552 | 0 | False | False | -900 | 0 | 0 | True | True | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| 24 | Nav | -552 | 0 | False | False | -876 | 352.8 | 360 | True | True | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| 48 | Nav | -552 | 0 | False | False | -732 | 352.8 | 360 | True | True | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| 51 | PhysicsOnly | -552 | 0 | False | False | -732 | 352.8 | 360 | True | True | 30 | -363.731 | 0 | 0 | 0 | 0 | 0 |
| 57 | PhysicsOnly | -545.778 | 93.147 | False | False | -684 | 352.8 | 360 | True | True | 30 | 418.322 | 0 | 0 | 0 | 0 | 0 |
| 60 | PhysicsOnly | -539.568 | 92.96 | False | False | -660 | 352.8 | 360 | True | True | 30 | 417.485 | 1 | 0 | 0 | 0 | 0 |
| 63 | PhysicsOnly | -539.568 | 92.96 | False | False | -660 | 352.8 | 360 | True | True | 30 | 417.485 | 1 | 3 | 0 | 0 | 0 |
| 81 | PhysicsOnly | -508.705 | 92.035 | False | False | -540 | 352.8 | 360 | True | True | 220 | 90.826 | 1 | 3 | 40 | 43 | 0 |
