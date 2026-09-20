# Static Obstacle Physics Showcase Benchmark

| Metric | Value |
| --- | ---: |
| Config source | `StaticObstaclePhysicsShowcaseMod:assets/StaticObstaclePhysicsShowcaseConfig.json` |
| Map | `static_obstacle_physics_showcase` |
| Regions | 4 |
| Obstacle entities | 1024 |
| Pieces per obstacle | 4 |
| Static rigid body descriptors | 4096 |
| Static body version after materialization | 1 |
| Steady-state dirty static bodies | 0 |
| Physics Hz | 60 |
| Last physics update ms | 0.9001 |
| Measured frames | 36 |
| Average frame tick ms | 5.1591 |
| Max frame tick ms | 137.0943 |

Production-chain evidence: ConfigPipeline catalog Replace entry -> map focus event -> RuntimeEntitySpawnQueue.EnqueueMany -> RuntimeEntitySpawnSystem -> ManifestationObstacleBridge2DSystem -> Physics2DSimulationSystem retained static cache.
