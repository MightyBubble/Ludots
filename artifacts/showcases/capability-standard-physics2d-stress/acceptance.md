# Capability Standard Physics2D Stress Acceptance

| Check | Evidence |
| --- | --- |
| Pure Physics2D startup | `physics2D.enabled=true`, `navigation2D.enabled=false`, no `Navigation2DRuntime` service |
| Spawn path | Config-driven RuntimeEntitySpawnQueue batch produced `256` dynamic bodies and `16` static columns |
| Throughput budget | avg measured tick `0.109` ms, budget `12` ms |
| Pipeline steady-state allocation | measured `54272` bytes over `48` frames, budget `65536` bytes |
| #358 blind spot closure | This is a pipeline-level measurement; the existing 0Alloc unit tests remain static hot-path guards and are not treated as endpoint throughput proof. |
| Physics stats | Hz `15`, potential pairs `68`, contact pairs `38`, last update `0.3241` ms |

## Keyframes

| Frame | Potential Pairs | Contact Pairs | Step Ms | Hash |
| ---: | ---: | ---: | ---: | ---: |
| 0 | 68 | 38 | 0.323 | 2495007005509193859 |
| 48 | 68 | 38 | 0.324 | -3119621281739792381 |
