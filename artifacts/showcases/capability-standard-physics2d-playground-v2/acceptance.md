# Capability Standard Physics2D Playground v2 Acceptance

| Check | Evidence |
| --- | --- |
| Startup boundary | `physics2D.enabled=true`, `navigation2D.enabled=true`, production `Navigation2DRuntime` present |
| Runtime boundary | v2 installs interaction only; production `Physics2DSimulationSystem` count remains 1 |
| Spawn path | `ConfigPipeline` catalog -> map focus event -> `RuntimeEntitySpawnQueue.EnqueueMany` -> `RuntimeEntitySpawnSystem` for 5 first-screen entities |
| Launch visibility | every first-screen entity has direct performer bootstrap; default camera frames the playground instead of the global debug grid |
| Physics-only mode | physics body has `Position2D/Velocity2D/Mass2D`, no `OrderBuffer`, no Nav components |
| CC/knockback regression | displacement under `MovementSuppressed2D` clears velocity and lands at the configured displacement distance |
| Nav mode | nav final X `-612` cm, desired X `360` cm/s, obstacle nav `True` physics `True` |
| v1 benchmark carryover | LeftShift+Q/W/E/R/T/Y/U/O/P count slots, right-click/runtime burst spawn, and C GAS force pulse retained; benchmark bodies `40`, sample Vx `-30.898` cm/s |
| Playground tools | HUD exposes FPS/frame/entity stats; G static polygons `1`, F friction zones `3`, X explosion affected `40` bodies |
| Physics stats | Hz `15`, potential pairs `34`, contact pairs `33`, last update `0.4187` ms |
| Test tick timings | frames `70`, avg `4.2939` ms, max `136.1888` ms |

## Keyframes

| Frame | Mode | Physics X | Physics Vx | Suppressed | Physics Has Nav | Nav X | Nav Vx | Nav Desired X | Nav Obstacle | Physics Obstacle | Benchmark Bodies | Benchmark Vx | Static Polygons | Friction Zones | Explosion Last |
| ---: | --- | ---: | ---: | :---: | :---: | ---: | ---: | ---: | :---: | :---: | ---: | ---: | ---: | ---: | ---: |
| 0 | PhysicsOnly | -712 | 119.76 | False | False | -900 | 0 | 0 | True | True | 0 | 0 | 0 | 0 | 0 |
| 7 | PhysicsOnly | -712 | 539.76 | False | False | -900 | 0 | 0 | True | True | 0 | 0 | 0 | 0 | 0 |
| 7 | PhysicsOnly | -712 | 0 | True | False | -900 | 0 | 0 | True | True | 0 | 0 | 0 | 0 | 0 |
| 18 | PhysicsOnly | -552 | 0 | False | False | -900 | 0 | 0 | True | True | 0 | 0 | 0 | 0 | 0 |
| 24 | Nav | -552 | 0 | False | False | -876 | 352.8 | 360 | True | True | 0 | 0 | 0 | 0 | 0 |
| 48 | Nav | -552 | 0 | False | False | -732 | 352.8 | 360 | True | True | 0 | 0 | 0 | 0 | 0 |
| 51 | PhysicsOnly | -552 | 0 | False | False | -732 | 352.8 | 360 | True | True | 30 | -363.731 | 0 | 0 | 0 |
| 57 | PhysicsOnly | -545.778 | 93.147 | False | False | -684 | 352.8 | 360 | True | True | 30 | 418.322 | 0 | 0 | 0 |
| 60 | PhysicsOnly | -539.568 | 92.96 | False | False | -660 | 352.8 | 360 | True | True | 30 | 417.485 | 1 | 0 | 0 |
| 63 | PhysicsOnly | -539.568 | 92.96 | False | False | -660 | 352.8 | 360 | True | True | 30 | 417.485 | 1 | 3 | 0 |
| 69 | PhysicsOnly | -527.186 | 92.589 | False | False | -612 | 352.8 | 360 | True | True | 40 | -30.898 | 1 | 3 | 40 |
