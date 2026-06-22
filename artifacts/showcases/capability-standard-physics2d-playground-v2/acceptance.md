# Capability Standard Physics2D Playground v2 Acceptance

| Check | Evidence |
| --- | --- |
| Startup boundary | `physics2D.enabled=true`, `navigation2D.enabled=true`, production `Navigation2DRuntime` present |
| Runtime boundary | v2 installs interaction only; production `Physics2DSimulationSystem` count remains 1 |
| Spawn path | `ConfigPipeline` catalog -> map focus event -> `RuntimeEntitySpawnQueue.EnqueueMany` -> `RuntimeEntitySpawnSystem` |
| Physics-only mode | physics body has `Position2D/Velocity2D/Mass2D`, no `OrderBuffer`, no Nav components |
| CC/knockback regression | displacement under `MovementSuppressed2D` clears velocity and lands at the configured displacement distance |
| Nav mode | nav final X `-732` cm, desired X `360` cm/s, obstacle nav `True` physics `True` |
| Physics stats | Hz `15`, potential pairs `0`, contact pairs `0`, last update `0.0210` ms |
| Test tick timings | frames `48`, avg `3.1444` ms, max `53.3804` ms |

## Keyframes

| Frame | Mode | Physics X | Physics Vx | Suppressed | Physics Has Nav | Nav X | Nav Vx | Nav Desired X | Nav Obstacle | Physics Obstacle |
| ---: | --- | ---: | ---: | :---: | :---: | ---: | ---: | ---: | :---: | :---: |
| 0 | PhysicsOnly | -712 | 119.76 | False | False | -900 | 0 | 0 | True | True |
| 7 | PhysicsOnly | -712 | 539.76 | False | False | -900 | 0 | 0 | True | True |
| 7 | PhysicsOnly | -712 | 0 | True | False | -900 | 0 | 0 | True | True |
| 18 | PhysicsOnly | -552 | 0 | False | False | -900 | 0 | 0 | True | True |
| 24 | Nav | -552 | 0 | False | False | -876 | 352.8 | 360 | True | True |
| 48 | Nav | -552 | 0 | False | False | -732 | 352.8 | 360 | True | True |
