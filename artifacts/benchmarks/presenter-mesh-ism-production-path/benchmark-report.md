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
| 3000 | 0.4494 ms | 69.4256 ms | 68.0465 ms | 0.7725 ms | 5.5223 ms | 1 | 3000 | 3000 | 3000 | 1 |
| 10000 | 2.0763 ms | 278.8590 ms | 278.2191 ms | 0.6350 ms | 8.6543 ms | 1 | 10000 | 10000 | 10000 | 1 |
| 30000 | 8.6530 ms | 1287.6669 ms | 1286.3893 ms | 1.2725 ms | 45.0682 ms | 1 | 30000 | 30000 | 30000 | 1 |

## Init Breakdown

| Count | init diag Total Tick | init diag Presentation | init diag Simulation | runtime batch | runtime prepare | runtime world create | runtime fill batch | runtime post spawn | runtime presenter batch | runtime presenter create | presenter setup | presenter world create | presenter component fill | presenter index write | presenter owner payload | presenter post create | runtime bootstrap mark | runtime presenters | init diag Camera Culling | init cull entity | init cull static | init cull pending remove | init cull dynamic | init cull presenter sync | init diag Behavior | init diag Animator | init diag Transform Sync | init diag Emit | init diag Emit Process | init diag Emit Cleanup | init dirty presenters | init diag Request Flush | init top presentation systems | init top simulation systems |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| 3000 | 68.0226 ms | 61.6573 ms | 6.3323 ms | 3000 | 0.1617 ms | 0.4865 ms | 0.8128 ms | 0.3464 ms | 2.7198 ms | 2.6415 ms | 0.1618 ms | 0.4537 ms | 0.8391 ms | 0.2728 ms | 0.0324 ms | 0.3540 ms | 0.0000 ms | 3000 | 19.1109 ms | 18.2940 ms | 8.2217 ms | 0.0000 ms | 0.0000 ms | 0.7684 ms | 0.0220 ms | 0.0028 ms | 0.0074 ms | 4.3038 ms | 4.2625 ms | 0.0000 ms | 3000 | 9.4111 ms | WorldToVisualSyncSystem 27.7453 ms; CameraCullingSystem 19.1123 ms; PresentationRequestFlushSystem 9.4125 ms | RuntimeEntitySpawnSystem 5.7833 ms; PresenterBlacksmithShowcaseKnowledgeProjectionSystem 0.1200 ms; ClearPresentationFlagsSystem 0.0429 ms |
| 10000 | 278.1909 ms | 230.0460 ms | 48.0975 ms | 10000 | 0.5550 ms | 19.9339 ms | 4.8218 ms | 1.0363 ms | 16.2971 ms | 16.0126 ms | 0.5731 ms | 7.8173 ms | 4.5968 ms | 0.8726 ms | 0.1089 ms | 0.9427 ms | 0.0000 ms | 10000 | 103.0621 ms | 100.0631 ms | 53.3363 ms | 0.0000 ms | 0.0000 ms | 2.9530 ms | 0.0187 ms | 0.0028 ms | 0.0083 ms | 16.8899 ms | 16.8304 ms | 0.0000 ms | 10000 | 2.4878 ms | WorldToVisualSyncSystem 104.5920 ms; CameraCullingSystem 103.0639 ms; PresenterEmitSystem 16.8908 ms | RuntimeEntitySpawnSystem 47.0737 ms; PresenterBlacksmithShowcaseKnowledgeProjectionSystem 0.4638 ms; ClearPresentationFlagsSystem 0.0562 ms |
| 30000 | 1286.3618 ms | 1151.6069 ms | 134.6335 ms | 30000 | 2.0128 ms | 21.4201 ms | 17.9916 ms | 3.9991 ms | 52.9007 ms | 51.8039 ms | 1.8835 ms | 24.8662 ms | 14.9253 ms | 3.8082 ms | 0.4086 ms | 2.9032 ms | 0.0000 ms | 30000 | 577.6441 ms | 571.1709 ms | 422.3269 ms | 0.0000 ms | 0.0000 ms | 6.4194 ms | 0.0249 ms | 0.0025 ms | 0.0102 ms | 45.4414 ms | 45.3737 ms | 0.0000 ms | 30000 | 8.6294 ms | CameraCullingSystem 577.6460 ms; WorldToVisualSyncSystem 511.5638 ms; PresenterEmitSystem 45.4429 ms | RuntimeEntitySpawnSystem 132.1426 ms; PresenterBlacksmithShowcaseKnowledgeProjectionSystem 1.8095 ms; Physics2DSimulationSystem 0.0573 ms |

## Stable Tick

| Count | Frames | Avg Tick | P95 Tick | Max Tick | Avg Bridge Sync | P95 Bridge Sync | Max Bridge Sync |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 3000 | 60 | 0.8152 ms | 1.2678 ms | 1.9422 ms | 0.0003 ms | 0.0007 ms | 0.0010 ms |
| 10000 | 60 | 3.0258 ms | 4.3211 ms | 4.6389 ms | 0.0014 ms | 0.0029 ms | 0.0030 ms |
| 30000 | 60 | 9.5496 ms | 12.1659 ms | 13.2691 ms | 0.0024 ms | 0.0034 ms | 0.0067 ms |

## Tick Breakdown

> `diag_*` values below come from `PresentationTimingDiagnostics` and are exponentially smoothed in-engine; use them as stable attribution, not exact per-frame wall-clock sums.

| Count | diag Total Tick | diag Presentation | diag Simulation | diag Camera Culling | diag cull entity | diag cull presenter sync | diag Behavior | diag Animator | diag Transform Sync | diag Emit | diag Request Flush |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 3000 | 1.3701 ms | 0.8319 ms | 0.5165 ms | 0.0609 ms | 0.0534 ms | 0.0022 ms | 0.0032 ms | 0.0004 ms | 0.0007 ms | 0.0192 ms | 0.5476 ms |
| 10000 | 4.2551 ms | 3.1985 ms | 0.9974 ms | 0.3086 ms | 0.2865 ms | 0.0086 ms | 0.0081 ms | 0.0008 ms | 0.0024 ms | 0.0674 ms | 2.0698 ms |
| 30000 | 14.9006 ms | 11.8437 ms | 2.9698 ms | 1.6425 ms | 1.6041 ms | 0.0186 ms | 0.0139 ms | 0.0012 ms | 0.0044 ms | 0.1573 ms | 7.2775 ms |

- 3000: init create+emit per entity `0.023142 ms`, runtime prepare `0.1617 ms`, world create `0.4865 ms`, fill batch `0.8128 ms`, post spawn `0.3464 ms`, presenter batch `2.7198 ms`, presenter create `2.6415 ms`, presenter setup `0.1618 ms`, presenter world create `0.4537 ms`, presenter component fill `0.8391 ms`, presenter index write `0.2728 ms`, presenter owner payload `0.0324 ms`, presenter post create `0.3540 ms`, bootstrap mark `0.0000 ms`, first tick `68.0465 ms`, validation scans `0.7725 ms`, init diag transform sync `0.0074 ms`, init diag emit `4.3038 ms`, dirty emit process `4.2625 ms`, dirty emit cleanup `0.0000 ms`, init diag request flush `9.4111 ms`, init diag culling `19.1109 ms`, init cull entity `18.2940 ms`, init cull static `8.2217 ms`, init cull pending remove `0.0000 ms`, init cull dynamic `0.0000 ms`, init cull presenter sync `0.7684 ms`, initial bridge sync per primitive `0.001841 ms`, stable avg tick per entity `0.000272 ms`, drops events `0` commands `0` primitives `0`
- 10000: init create+emit per entity `0.027886 ms`, runtime prepare `0.5550 ms`, world create `19.9339 ms`, fill batch `4.8218 ms`, post spawn `1.0363 ms`, presenter batch `16.2971 ms`, presenter create `16.0126 ms`, presenter setup `0.5731 ms`, presenter world create `7.8173 ms`, presenter component fill `4.5968 ms`, presenter index write `0.8726 ms`, presenter owner payload `0.1089 ms`, presenter post create `0.9427 ms`, bootstrap mark `0.0000 ms`, first tick `278.2191 ms`, validation scans `0.6350 ms`, init diag transform sync `0.0083 ms`, init diag emit `16.8899 ms`, dirty emit process `16.8304 ms`, dirty emit cleanup `0.0000 ms`, init diag request flush `2.4878 ms`, init diag culling `103.0621 ms`, init cull entity `100.0631 ms`, init cull static `53.3363 ms`, init cull pending remove `0.0000 ms`, init cull dynamic `0.0000 ms`, init cull presenter sync `2.9530 ms`, initial bridge sync per primitive `0.000865 ms`, stable avg tick per entity `0.000303 ms`, drops events `0` commands `0` primitives `0`
- 30000: init create+emit per entity `0.042922 ms`, runtime prepare `2.0128 ms`, world create `21.4201 ms`, fill batch `17.9916 ms`, post spawn `3.9991 ms`, presenter batch `52.9007 ms`, presenter create `51.8039 ms`, presenter setup `1.8835 ms`, presenter world create `24.8662 ms`, presenter component fill `14.9253 ms`, presenter index write `3.8082 ms`, presenter owner payload `0.4086 ms`, presenter post create `2.9032 ms`, bootstrap mark `0.0000 ms`, first tick `1286.3893 ms`, validation scans `1.2725 ms`, init diag transform sync `0.0102 ms`, init diag emit `45.4414 ms`, dirty emit process `45.3737 ms`, dirty emit cleanup `0.0000 ms`, init diag request flush `8.6294 ms`, init diag culling `577.6441 ms`, init cull entity `571.1709 ms`, init cull static `422.3269 ms`, init cull pending remove `0.0000 ms`, init cull dynamic `0.0000 ms`, init cull presenter sync `6.4194 ms`, initial bridge sync per primitive `0.001502 ms`, stable avg tick per entity `0.000318 ms`, drops events `0` commands `0` primitives `0`
