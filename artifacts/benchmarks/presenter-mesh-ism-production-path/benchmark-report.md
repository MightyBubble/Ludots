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
| 3000 | 0.8034 ms | 42.8371 ms | 41.9312 ms | 0.5369 ms | 7.0002 ms | 1 | 3000 | 3000 | 3000 | 1 |
| 10000 | 2.7529 ms | 184.4764 ms | 184.0404 ms | 0.4321 ms | 6.1602 ms | 1 | 10000 | 10000 | 10000 | 1 |
| 30000 | 7.0920 ms | 877.8469 ms | 876.8093 ms | 1.0331 ms | 27.2111 ms | 1 | 30000 | 30000 | 30000 | 1 |

## Init Breakdown

| Count | init diag Total Tick | init diag Presentation | init diag Simulation | runtime batch | runtime prepare | runtime world create | runtime fill batch | runtime post spawn | runtime presenter batch | runtime presenter create | presenter setup | presenter world create | presenter component fill | presenter index write | presenter owner payload | presenter post create | runtime bootstrap mark | runtime presenters | init diag Camera Culling | init cull entity | init cull static | init cull pending remove | init cull dynamic | init cull presenter sync | init diag Behavior | init diag Animator | init diag Transform Sync | init diag Emit | init diag Emit Process | init diag Emit Cleanup | init dirty presenters | init diag Request Flush | init top presentation systems | init top simulation systems |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| 3000 | 41.9118 ms | 36.7391 ms | 5.1492 ms | 3000 | 0.1205 ms | 0.3597 ms | 0.7689 ms | 0.2153 ms | 2.1250 ms | 2.0545 ms | 0.1314 ms | 0.3468 ms | 0.9756 ms | 0.2046 ms | 0.0265 ms | 0.1689 ms | 0.0000 ms | 3000 | 12.4930 ms | 11.8681 ms | 5.5658 ms | 0.0000 ms | 0.0000 ms | 0.6205 ms | 0.0137 ms | 0.0027 ms | 0.0060 ms | 3.1896 ms | 3.1581 ms | 0.0000 ms | 3000 | 0.6115 ms | WorldToVisualSyncSystem 19.6348 ms; CameraCullingSystem 12.4941 ms; PresenterEmitSystem 3.1907 ms | RuntimeEntitySpawnSystem 4.7811 ms; PresenterBlacksmithShowcaseKnowledgeProjectionSystem 0.0864 ms; ClearPresentationFlagsSystem 0.0264 ms |
| 10000 | 184.0181 ms | 140.8341 ms | 43.1502 ms | 10000 | 0.5643 ms | 16.2242 ms | 3.2322 ms | 0.7834 ms | 15.7253 ms | 15.5183 ms | 0.3914 ms | 8.9295 ms | 4.0315 ms | 0.6435 ms | 0.1123 ms | 0.6671 ms | 0.0000 ms | 10000 | 70.6691 ms | 68.1326 ms | 37.5846 ms | 0.0000 ms | 0.0000 ms | 2.5314 ms | 0.0258 ms | 0.0031 ms | 0.0146 ms | 12.4913 ms | 12.4300 ms | 0.0000 ms | 10000 | 1.9820 ms | CameraCullingSystem 70.6704 ms; WorldToVisualSyncSystem 52.5372 ms; PresenterEmitSystem 12.4925 ms | RuntimeEntitySpawnSystem 42.3239 ms; PresenterBlacksmithShowcaseKnowledgeProjectionSystem 0.3492 ms; ClearPresentationFlagsSystem 0.0533 ms |
| 30000 | 876.7859 ms | 783.2192 ms | 93.4659 ms | 30000 | 1.2248 ms | 30.6784 ms | 7.8503 ms | 2.1281 ms | 38.5192 ms | 37.9863 ms | 1.0675 ms | 21.0683 ms | 10.0062 ms | 1.7487 ms | 0.2427 ms | 1.7080 ms | 0.0000 ms | 30000 | 387.6299 ms | 382.8586 ms | 296.4766 ms | 0.0000 ms | 0.0000 ms | 4.7662 ms | 0.0154 ms | 0.0022 ms | 0.0070 ms | 30.1117 ms | 30.0598 ms | 0.0000 ms | 30000 | 6.5659 ms | CameraCullingSystem 387.6310 ms; WorldToVisualSyncSystem 353.2718 ms; PresenterEmitSystem 30.1127 ms | RuntimeEntitySpawnSystem 92.1628 ms; PresenterBlacksmithShowcaseKnowledgeProjectionSystem 0.7139 ms; Physics2DSimulationSystem 0.0481 ms |

## Stable Tick

| Count | Frames | Avg Tick | P95 Tick | Max Tick | Avg Bridge Sync | P95 Bridge Sync | Max Bridge Sync |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 3000 | 60 | 0.5903 ms | 0.8975 ms | 1.6400 ms | 0.0002 ms | 0.0006 ms | 0.0010 ms |
| 10000 | 60 | 1.7387 ms | 2.2852 ms | 2.7832 ms | 0.0002 ms | 0.0006 ms | 0.0009 ms |
| 30000 | 60 | 5.5189 ms | 7.2475 ms | 7.9265 ms | 0.0016 ms | 0.0021 ms | 0.0027 ms |

## Tick Breakdown

> `diag_*` values below come from `PresentationTimingDiagnostics` and are exponentially smoothed in-engine; use them as stable attribution, not exact per-frame wall-clock sums.

| Count | diag Total Tick | diag Presentation | diag Simulation | diag Camera Culling | diag cull entity | diag cull presenter sync | diag Behavior | diag Animator | diag Transform Sync | diag Emit | diag Request Flush |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 3000 | 0.7085 ms | 0.5938 ms | 0.0982 ms | 0.0409 ms | 0.0383 ms | 0.0018 ms | 0.0030 ms | 0.0003 ms | 0.0008 ms | 0.0148 ms | 0.3994 ms |
| 10000 | 2.2366 ms | 1.8975 ms | 0.3150 ms | 0.2052 ms | 0.1970 ms | 0.0072 ms | 0.0032 ms | 0.0002 ms | 0.0007 ms | 0.0422 ms | 1.2925 ms |
| 30000 | 7.8726 ms | 7.1338 ms | 0.6800 ms | 1.1029 ms | 1.0867 ms | 0.0136 ms | 0.0073 ms | 0.0007 ms | 0.0022 ms | 0.1036 ms | 4.2352 ms |

- 3000: init create+emit per entity `0.014279 ms`, runtime prepare `0.1205 ms`, world create `0.3597 ms`, fill batch `0.7689 ms`, post spawn `0.2153 ms`, presenter batch `2.1250 ms`, presenter create `2.0545 ms`, presenter setup `0.1314 ms`, presenter world create `0.3468 ms`, presenter component fill `0.9756 ms`, presenter index write `0.2046 ms`, presenter owner payload `0.0265 ms`, presenter post create `0.1689 ms`, bootstrap mark `0.0000 ms`, first tick `41.9312 ms`, validation scans `0.5369 ms`, init diag transform sync `0.0060 ms`, init diag emit `3.1896 ms`, dirty emit process `3.1581 ms`, dirty emit cleanup `0.0000 ms`, init diag request flush `0.6115 ms`, init diag culling `12.4930 ms`, init cull entity `11.8681 ms`, init cull static `5.5658 ms`, init cull pending remove `0.0000 ms`, init cull dynamic `0.0000 ms`, init cull presenter sync `0.6205 ms`, initial bridge sync per primitive `0.002333 ms`, stable avg tick per entity `0.000197 ms`, drops events `0` commands `0` primitives `0`
- 10000: init create+emit per entity `0.018448 ms`, runtime prepare `0.5643 ms`, world create `16.2242 ms`, fill batch `3.2322 ms`, post spawn `0.7834 ms`, presenter batch `15.7253 ms`, presenter create `15.5183 ms`, presenter setup `0.3914 ms`, presenter world create `8.9295 ms`, presenter component fill `4.0315 ms`, presenter index write `0.6435 ms`, presenter owner payload `0.1123 ms`, presenter post create `0.6671 ms`, bootstrap mark `0.0000 ms`, first tick `184.0404 ms`, validation scans `0.4321 ms`, init diag transform sync `0.0146 ms`, init diag emit `12.4913 ms`, dirty emit process `12.4300 ms`, dirty emit cleanup `0.0000 ms`, init diag request flush `1.9820 ms`, init diag culling `70.6691 ms`, init cull entity `68.1326 ms`, init cull static `37.5846 ms`, init cull pending remove `0.0000 ms`, init cull dynamic `0.0000 ms`, init cull presenter sync `2.5314 ms`, initial bridge sync per primitive `0.000616 ms`, stable avg tick per entity `0.000174 ms`, drops events `0` commands `0` primitives `0`
- 30000: init create+emit per entity `0.029262 ms`, runtime prepare `1.2248 ms`, world create `30.6784 ms`, fill batch `7.8503 ms`, post spawn `2.1281 ms`, presenter batch `38.5192 ms`, presenter create `37.9863 ms`, presenter setup `1.0675 ms`, presenter world create `21.0683 ms`, presenter component fill `10.0062 ms`, presenter index write `1.7487 ms`, presenter owner payload `0.2427 ms`, presenter post create `1.7080 ms`, bootstrap mark `0.0000 ms`, first tick `876.8093 ms`, validation scans `1.0331 ms`, init diag transform sync `0.0070 ms`, init diag emit `30.1117 ms`, dirty emit process `30.0598 ms`, dirty emit cleanup `0.0000 ms`, init diag request flush `6.5659 ms`, init diag culling `387.6299 ms`, init cull entity `382.8586 ms`, init cull static `296.4766 ms`, init cull pending remove `0.0000 ms`, init cull dynamic `0.0000 ms`, init cull presenter sync `4.7662 ms`, initial bridge sync per primitive `0.000907 ms`, stable avg tick per entity `0.000184 ms`, drops events `0` commands `0` primitives `0`
