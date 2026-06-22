# Capability Standard Physics2D Acceptance

| Check | Evidence |
| --- | --- |
| Pure Physics2D startup | `physics2D.enabled=true`, `navigation2D.enabled=false`, no `Navigation2DRuntime` service |
| Spawn path | `ConfigPipeline` catalog -> map focus event -> `RuntimeEntitySpawnQueue.EnqueueMany` -> `RuntimeEntitySpawnSystem` |
| Static polygon wall | Static body version `1`, descriptors `3` |
| Restitution bounce | final stone velocity X `-681.165` cm/s |
| ForceInput knockback | frame 1 force X/Y `0` / `0`, velocity X `59.7` cm/s |
| Damping field | final damping probe velocity X `1.934` cm/s, applied damping `0.55` |
| Kinematic rotating door | final rotation `0.72` rad |
| Friction tangent impulse | low friction X `83.68` / Vx `726.4`, high friction X `-289` / Vx `-290` |
| Radial impulse symmetry | east/west Vx `240` / `-240`, north/south Vy `240` / `-240` |
| Physics stats | Hz `15`, potential pairs `0`, contact pairs `0`, last update `0.0000` ms |
| Test tick timings | frames `40`, avg `0.3137` ms, max `4.2309` ms |

## Keyframes

| Frame | Stone X | Stone Vx | Knockback X | Knockback Vx | Damping Vx | Door Rot |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 0 | -312 | 720 | -896 | 59.7 | 231 | 0.08 |
| 1 | -312 | 720 | -896 | 59.7 | 231 | 0.08 |
| 24 | -179.386 | -681.165 | -876.298 | 58.222 | 11.626 | 0.48 |
| 40 | -315.619 | -681.165 | -864.712 | 57.353 | 1.934 | 0.72 |
