# Capability Standard Nav Sink 2D Acceptance

| Check | Evidence |
| --- | --- |
| Startup boundary | `physics2D.enabled=true`, `navigation2D.enabled=true`, `Navigation2DRuntime` service present |
| Spawn path | `ConfigPipeline` catalog -> map focus event -> `RuntimeEntitySpawnQueue.EnqueueMany` -> `RuntimeEntitySpawnSystem` |
| Agent boundary | template authors order/input facts; `NavOrderAgentBootstrapSystem` derives `NavAgent2D`, `Position2D`, `Velocity2D`, `NavKinematics2D` |
| Nav steering boundary | active `moveTo` order produces `NavDesiredVelocity2D`; Physics2D sync commits `Velocity2D` |
| Obstacle bridge | authored `ManifestationObstacleIntent2D` derives `Collider2D`, `Physics2DStaticBodyState`, and `NavObstacle2D` |
| Position authority | final Position2D X `-732` cm, WorldPositionCm synced from Physics2D |
| Runtime counts | nav agents `1`, obstacle nav `True`, obstacle physics `True` |
| Physics stats | Hz `15`, potential pairs `0`, contact pairs `0`, last update `0.0230` ms |
| Test tick timings | frames `36`, avg `1.1937` ms, max `16.5361` ms |

## Keyframes

| Frame | Agent X | Desired X | Velocity X | Goal | Nav Obstacle | Physics Obstacle | Nav Agents |
| ---: | ---: | ---: | ---: | :---: | :---: | :---: | ---: |
| 0 | -900 | 0 | 0 | False | True | True | 1 |
| 9 | -900 | 0 | 0 | True | True | True | 1 |
| 12 | -876 | 360 | 352.8 | True | True | True | 1 |
| 36 | -732 | 360 | 352.8 | True | True | True | 1 |
