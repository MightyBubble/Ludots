# Capability Standard Knockback2D Acceptance

| Check | Evidence |
| --- | --- |
| Startup boundary | `physics2D.enabled=true`, `navigation2D.enabled=false`, no `Navigation2DRuntime` service |
| Spawn path | `ConfigPipeline` catalog -> map focus event -> `RuntimeEntitySpawnQueue.EnqueueMany` -> `RuntimeEntitySpawnSystem` |
| Static AwayFromSource displacement | target moved exactly 180 cm on X through `DisplacementRuntimeSystem` |
| Moving CC no drift | suppressed target advanced 4 x 50 cm displacement steps with `Velocity2D.Linear=0` each frame |
| CC recovery | next sync restored velocity X `120` cm/s from `NavDesiredVelocity2D` |
| Wall correction | knockback into static wall stayed bounded by Physics2D position correction |
| Physics stats | Hz `15`, potential pairs `1`, contact pairs `1`, last update `0.0295` ms |
| Test tick timings | frames `62`, avg `4.9418` ms, max `94.3565` ms |

## Keyframes

| Frame | Moving X | Moving Vx | Suppressed | Static X | Wall Target X |
| ---: | ---: | ---: | :---: | ---: | ---: |
| 6 | -420 | 120 | False | 140 | 100 |
| 21 | -380 | 120 | False | 320 | 100 |
| 25 | -330 | 0 | True | 320 | 100 |
| 26 | -330 | 0 | True | 320 | 100 |
| 27 | -280 | 0 | True | 320 | 100 |
| 28 | -280 | 0 | True | 320 | 100 |
| 29 | -280 | 0 | True | 320 | 100 |
| 30 | -230 | 0 | True | 320 | 100 |
| 31 | -230 | 0 | True | 320 | 100 |
| 32 | -230 | 0 | True | 320 | 100 |
| 33 | -180 | 0 | False | 320 | 100 |
| 61 | -124 | 120 | False | 320 | 178.384 |
