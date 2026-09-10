# Presenter Mesh ISM Production Path Benchmark

- template: `blacksmith_mesh_benchmark_entity`
- presenter rule: `EntitySpawned -> blacksmith_mesh_benchmark_ism`
- mesh: `blacksmith.building.north.intact`
- render path: `InstancedStaticMesh` through `RaylibIsmRenderBridge.SyncPersistentLanes`
- init excludes: real GPU draw call timing; validates production create/first emit plus latest raylib ISM bridge bucketing
- tick excludes: real GPU draw call timing; validates stable production tick plus raylib bridge resync cost after initialization
- stable tick sampling starts after explicit post-init GC cleanup and warmup frames so init debt does not pollute steady-state numbers

## Init

| Count | Enqueue | Create+First Emit | First Tick | Validation Scans | Raylib Initial Sync | Settle Frames | Entities | Presenters | ISM Primitives | Raylib Buckets |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 3000 | 0.4644 ms | 52.1139 ms | 51.0489 ms | 0.6963 ms | 4.0627 ms | 1 | 3000 | 3000 | 3000 | 1 |
| 10000 | 2.8724 ms | 110.8905 ms | 110.1949 ms | 0.6871 ms | 8.2621 ms | 1 | 10000 | 10000 | 10000 | 1 |
| 30000 | 5.5739 ms | 727.7622 ms | 725.6554 ms | 2.1004 ms | 12.9158 ms | 1 | 30000 | 30000 | 30000 | 1 |

## Init Breakdown

| Count | init diag Total Tick | init diag Presentation | init diag Simulation | runtime batch | runtime prepare | runtime world create | runtime fill batch | runtime post spawn | runtime presenter batch | runtime presenter create | presenter setup | presenter world create | presenter component fill | presenter index write | presenter owner payload | presenter post create | runtime bootstrap mark | runtime presenters | init diag Camera Culling | init cull entity | init cull static | init cull pending remove | init cull dynamic | init cull presenter sync | init diag Behavior | init diag Animator | init diag Transform Sync | init diag Emit | init diag Emit Process | init diag Emit Cleanup | init dirty presenters | init diag Request Flush | init top presentation systems | init top simulation systems |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| 3000 | 51.0374 ms | 18.2078 ms | 32.8121 ms | 3000 | 0.0896 ms | 0.3504 ms | 0.5795 ms | 0.0578 ms | 11.1175 ms | 11.0656 ms | 0.0913 ms | 10.1020 ms | 0.2704 ms | 0.1228 ms | 0.0204 ms | 0.3902 ms | 0.0000 ms | 3000 | 7.8651 ms | 7.7142 ms | 4.1145 ms | 0.0000 ms | 0.0000 ms | 0.1498 ms | 0.0084 ms | 0.0019 ms | 0.0027 ms | 1.8304 ms | 1.8152 ms | 0.0000 ms | 3000 | 0.4155 ms | CameraCullingSystem 7.8675 ms; WorldToVisualSyncSystem 7.8019 ms; PresenterEmitSystem 1.8317 ms | MapEntityLifecycleObserverSystem 19.1149 ms; RuntimeEntitySpawnSystem 13.3086 ms; PresenterBlacksmithShowcaseKnowledgeProjectionSystem 0.0791 ms |
| 10000 | 110.1850 ms | 63.1999 ms | 46.9714 ms | 10000 | 0.1837 ms | 12.7582 ms | 1.0922 ms | 0.1816 ms | 10.2201 ms | 10.1471 ms | 0.1928 ms | 7.5525 ms | 0.8309 ms | 0.3241 ms | 0.0545 ms | 1.0862 ms | 0.0000 ms | 10000 | 31.1626 ms | 30.8097 ms | 20.4316 ms | 0.0000 ms | 0.0000 ms | 0.3517 ms | 0.0084 ms | 0.0009 ms | 0.0034 ms | 4.7645 ms | 4.7494 ms | 0.0000 ms | 10000 | 1.5536 ms | CameraCullingSystem 31.1648 ms; WorldToVisualSyncSystem 25.1638 ms; PresenterEmitSystem 4.7658 ms | RuntimeEntitySpawnSystem 26.7122 ms; MapEntityLifecycleObserverSystem 19.8906 ms; PresenterBlacksmithShowcaseKnowledgeProjectionSystem 0.1361 ms |
| 30000 | 725.6444 ms | 634.6462 ms | 90.9656 ms | 30000 | 0.8771 ms | 19.9542 ms | 3.8358 ms | 0.5299 ms | 31.2904 ms | 31.1270 ms | 0.4149 ms | 22.4382 ms | 3.6491 ms | 1.0242 ms | 0.1914 ms | 3.0730 ms | 0.0000 ms | 30000 | 312.7240 ms | 311.8068 ms | 259.9127 ms | 0.0000 ms | 0.0000 ms | 0.9159 ms | 0.0077 ms | 0.0007 ms | 0.0030 ms | 13.2701 ms | 13.2520 ms | 0.0000 ms | 30000 | 4.2947 ms | CameraCullingSystem 312.7265 ms; WorldToVisualSyncSystem 303.3418 ms; PresenterEmitSystem 13.2725 ms | RuntimeEntitySpawnSystem 73.8786 ms; MapEntityLifecycleObserverSystem 16.4118 ms; PresenterBlacksmithShowcaseKnowledgeProjectionSystem 0.3840 ms |

## Stable Tick

| Count | Frames | Avg Tick | P95 Tick | Max Tick | Avg Bridge Sync | P95 Bridge Sync | Max Bridge Sync |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 3000 | 60 | 0.3285 ms | 0.4455 ms | 0.6928 ms | 0.0004 ms | 0.0022 ms | 0.0073 ms |
| 10000 | 60 | 0.8181 ms | 1.1568 ms | 1.6389 ms | 0.0001 ms | 0.0004 ms | 0.0004 ms |
| 30000 | 60 | 3.8545 ms | 4.9288 ms | 5.0750 ms | 0.0013 ms | 0.0098 ms | 0.0141 ms |

## Tick Breakdown

> `diag_*` values below come from `PresentationTimingDiagnostics` and are exponentially smoothed in-engine; use them as stable attribution, not exact per-frame wall-clock sums.

| Count | diag Total Tick | diag Presentation | diag Simulation | diag Camera Culling | diag cull entity | diag cull presenter sync | diag Behavior | diag Animator | diag Transform Sync | diag Emit | diag Request Flush |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 3000 | 0.4803 ms | 0.3342 ms | 0.1417 ms | 0.0239 ms | 0.0232 ms | 0.0023 ms | 0.0010 ms | 0.0001 ms | 0.0002 ms | 0.0065 ms | 0.2508 ms |
| 10000 | 1.1200 ms | 0.8692 ms | 0.2464 ms | 0.0886 ms | 0.0876 ms | 0.0054 ms | 0.0010 ms | 0.0001 ms | 0.0002 ms | 0.0146 ms | 0.6379 ms |
| 30000 | 5.8050 ms | 5.1406 ms | 0.6417 ms | 0.8822 ms | 0.8789 ms | 0.0144 ms | 0.0032 ms | 0.0003 ms | 0.0010 ms | 0.0414 ms | 3.0454 ms |

- 3000: init create+emit per entity `0.017371 ms`, runtime prepare `0.0896 ms`, world create `0.3504 ms`, fill batch `0.5795 ms`, post spawn `0.0578 ms`, presenter batch `11.1175 ms`, presenter create `11.0656 ms`, presenter setup `0.0913 ms`, presenter world create `10.1020 ms`, presenter component fill `0.2704 ms`, presenter index write `0.1228 ms`, presenter owner payload `0.0204 ms`, presenter post create `0.3902 ms`, bootstrap mark `0.0000 ms`, first tick `51.0489 ms`, validation scans `0.6963 ms`, init diag transform sync `0.0027 ms`, init diag emit `1.8304 ms`, dirty emit process `1.8152 ms`, dirty emit cleanup `0.0000 ms`, init diag request flush `0.4155 ms`, init diag culling `7.8651 ms`, init cull entity `7.7142 ms`, init cull static `4.1145 ms`, init cull pending remove `0.0000 ms`, init cull dynamic `0.0000 ms`, init cull presenter sync `0.1498 ms`, initial bridge sync per primitive `0.001354 ms`, stable avg tick per entity `0.000109 ms`, drops events `0` commands `0` primitives `0`
- 10000: init create+emit per entity `0.011089 ms`, runtime prepare `0.1837 ms`, world create `12.7582 ms`, fill batch `1.0922 ms`, post spawn `0.1816 ms`, presenter batch `10.2201 ms`, presenter create `10.1471 ms`, presenter setup `0.1928 ms`, presenter world create `7.5525 ms`, presenter component fill `0.8309 ms`, presenter index write `0.3241 ms`, presenter owner payload `0.0545 ms`, presenter post create `1.0862 ms`, bootstrap mark `0.0000 ms`, first tick `110.1949 ms`, validation scans `0.6871 ms`, init diag transform sync `0.0034 ms`, init diag emit `4.7645 ms`, dirty emit process `4.7494 ms`, dirty emit cleanup `0.0000 ms`, init diag request flush `1.5536 ms`, init diag culling `31.1626 ms`, init cull entity `30.8097 ms`, init cull static `20.4316 ms`, init cull pending remove `0.0000 ms`, init cull dynamic `0.0000 ms`, init cull presenter sync `0.3517 ms`, initial bridge sync per primitive `0.000826 ms`, stable avg tick per entity `0.000082 ms`, drops events `0` commands `0` primitives `0`
- 30000: init create+emit per entity `0.024259 ms`, runtime prepare `0.8771 ms`, world create `19.9542 ms`, fill batch `3.8358 ms`, post spawn `0.5299 ms`, presenter batch `31.2904 ms`, presenter create `31.1270 ms`, presenter setup `0.4149 ms`, presenter world create `22.4382 ms`, presenter component fill `3.6491 ms`, presenter index write `1.0242 ms`, presenter owner payload `0.1914 ms`, presenter post create `3.0730 ms`, bootstrap mark `0.0000 ms`, first tick `725.6554 ms`, validation scans `2.1004 ms`, init diag transform sync `0.0030 ms`, init diag emit `13.2701 ms`, dirty emit process `13.2520 ms`, dirty emit cleanup `0.0000 ms`, init diag request flush `4.2947 ms`, init diag culling `312.7240 ms`, init cull entity `311.8068 ms`, init cull static `259.9127 ms`, init cull pending remove `0.0000 ms`, init cull dynamic `0.0000 ms`, init cull presenter sync `0.9159 ms`, initial bridge sync per primitive `0.000431 ms`, stable avg tick per entity `0.000128 ms`, drops events `0` commands `0` primitives `0`
