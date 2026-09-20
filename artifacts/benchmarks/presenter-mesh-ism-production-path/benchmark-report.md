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
| 3000 | 0.8546 ms | 103.0414 ms | 101.2036 ms | 1.2423 ms | 8.0268 ms | 1 | 3000 | 3000 | 3000 | 1 |
| 10000 | 1.6183 ms | 299.0955 ms | 298.5338 ms | 0.5568 ms | 9.6658 ms | 1 | 10000 | 10000 | 10000 | 1 |
| 30000 | 10.0402 ms | 1649.2335 ms | 1647.6047 ms | 1.6247 ms | 21.1215 ms | 1 | 30000 | 30000 | 30000 | 1 |

## Init Breakdown

| Count | init diag Total Tick | init diag Presentation | init diag Simulation | runtime batch | runtime prepare | runtime world create | runtime fill batch | runtime post spawn | runtime presenter batch | runtime presenter create | presenter setup | presenter world create | presenter component fill | presenter index write | presenter owner payload | presenter post create | runtime bootstrap mark | runtime presenters | init diag Camera Culling | init cull entity | init cull static | init cull pending remove | init cull dynamic | init cull presenter sync | init diag Behavior | init diag Animator | init diag Transform Sync | init diag Emit | init diag Emit Process | init diag Emit Cleanup | init dirty presenters | init diag Request Flush | init top presentation systems | init top simulation systems |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| 3000 | 101.1834 ms | 51.5897 ms | 49.5679 ms | 3000 | 0.2779 ms | 0.5609 ms | 1.1313 ms | 0.5070 ms | 8.8972 ms | 8.7824 ms | 0.2253 ms | 5.7509 ms | 1.5834 ms | 0.4085 ms | 0.0390 ms | 0.3613 ms | 0.0000 ms | 3000 | 21.6045 ms | 21.0980 ms | 9.6588 ms | 0.0000 ms | 0.0000 ms | 0.5013 ms | 0.0187 ms | 0.0033 ms | 0.0091 ms | 5.2728 ms | 5.2380 ms | 0.0000 ms | 3000 | 1.0071 ms | WorldToVisualSyncSystem 22.7198 ms; CameraCullingSystem 21.6053 ms; PresenterEmitSystem 5.2737 ms | MapEntityLifecycleObserverSystem 36.3398 ms; RuntimeEntitySpawnSystem 12.6536 ms; PresenterBlacksmithShowcaseKnowledgeProjectionSystem 0.1645 ms |
| 10000 | 298.5084 ms | 204.2057 ms | 94.2804 ms | 10000 | 0.7421 ms | 18.6074 ms | 4.3713 ms | 2.7404 ms | 20.9475 ms | 20.5016 ms | 0.7170 ms | 9.8648 ms | 6.1393 ms | 1.0864 ms | 0.1352 ms | 1.1439 ms | 0.0000 ms | 10000 | 94.5516 ms | 91.2768 ms | 48.7989 ms | 0.0000 ms | 0.0000 ms | 3.2686 ms | 0.0235 ms | 0.0036 ms | 0.0098 ms | 16.2182 ms | 16.1714 ms | 0.0000 ms | 10000 | 7.6978 ms | CameraCullingSystem 94.5530 ms; WorldToVisualSyncSystem 82.6601 ms; PresenterEmitSystem 16.2196 ms | RuntimeEntitySpawnSystem 52.4054 ms; MapEntityLifecycleObserverSystem 40.7669 ms; PresenterBlacksmithShowcaseKnowledgeProjectionSystem 0.5686 ms |
| 30000 | 1647.5807 ms | 1425.2078 ms | 222.3161 ms | 30000 | 3.0789 ms | 22.9129 ms | 18.5155 ms | 10.7623 ms | 72.8890 ms | 71.0444 ms | 2.6325 ms | 32.8714 ms | 20.2561 ms | 4.9518 ms | 0.5201 ms | 4.4931 ms | 0.0000 ms | 30000 | 688.7353 ms | 679.2253 ms | 511.1488 ms | 0.0000 ms | 0.0000 ms | 9.5033 ms | 0.0225 ms | 0.0034 ms | 0.0122 ms | 45.7009 ms | 45.6389 ms | 0.0000 ms | 30000 | 8.9921 ms | CameraCullingSystem 688.7363 ms; WorldToVisualSyncSystem 671.3331 ms; PresenterEmitSystem 45.7023 ms | RuntimeEntitySpawnSystem 167.5854 ms; MapEntityLifecycleObserverSystem 51.8750 ms; PresenterBlacksmithShowcaseKnowledgeProjectionSystem 2.1460 ms |

## Stable Tick

| Count | Frames | Avg Tick | P95 Tick | Max Tick | Avg Bridge Sync | P95 Bridge Sync | Max Bridge Sync |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 3000 | 60 | 1.0659 ms | 1.5785 ms | 2.1259 ms | 0.0003 ms | 0.0006 ms | 0.0007 ms |
| 10000 | 60 | 3.1754 ms | 4.2096 ms | 5.3032 ms | 0.0004 ms | 0.0011 ms | 0.0011 ms |
| 30000 | 60 | 10.7157 ms | 18.6613 ms | 20.5493 ms | 0.0014 ms | 0.0026 ms | 0.0030 ms |

## Tick Breakdown

> `diag_*` values below come from `PresentationTimingDiagnostics` and are exponentially smoothed in-engine; use them as stable attribution, not exact per-frame wall-clock sums.

| Count | diag Total Tick | diag Presentation | diag Simulation | diag Camera Culling | diag cull entity | diag cull presenter sync | diag Behavior | diag Animator | diag Transform Sync | diag Emit | diag Request Flush |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 3000 | 1.3408 ms | 0.9709 ms | 0.3427 ms | 0.0701 ms | 0.0671 ms | 0.0015 ms | 0.0038 ms | 0.0005 ms | 0.0010 ms | 0.0237 ms | 0.6759 ms |
| 10000 | 3.9621 ms | 3.1719 ms | 0.7577 ms | 0.2766 ms | 0.2657 ms | 0.0093 ms | 0.0043 ms | 0.0004 ms | 0.0012 ms | 0.0563 ms | 2.2487 ms |
| 30000 | 15.2283 ms | 12.8678 ms | 2.3015 ms | 1.9436 ms | 1.9140 ms | 0.0269 ms | 0.0080 ms | 0.0007 ms | 0.0023 ms | 0.1480 ms | 7.7014 ms |

- 3000: init create+emit per entity `0.034347 ms`, runtime prepare `0.2779 ms`, world create `0.5609 ms`, fill batch `1.1313 ms`, post spawn `0.5070 ms`, presenter batch `8.8972 ms`, presenter create `8.7824 ms`, presenter setup `0.2253 ms`, presenter world create `5.7509 ms`, presenter component fill `1.5834 ms`, presenter index write `0.4085 ms`, presenter owner payload `0.0390 ms`, presenter post create `0.3613 ms`, bootstrap mark `0.0000 ms`, first tick `101.2036 ms`, validation scans `1.2423 ms`, init diag transform sync `0.0091 ms`, init diag emit `5.2728 ms`, dirty emit process `5.2380 ms`, dirty emit cleanup `0.0000 ms`, init diag request flush `1.0071 ms`, init diag culling `21.6045 ms`, init cull entity `21.0980 ms`, init cull static `9.6588 ms`, init cull pending remove `0.0000 ms`, init cull dynamic `0.0000 ms`, init cull presenter sync `0.5013 ms`, initial bridge sync per primitive `0.002676 ms`, stable avg tick per entity `0.000355 ms`, drops events `0` commands `0` primitives `0`
- 10000: init create+emit per entity `0.029910 ms`, runtime prepare `0.7421 ms`, world create `18.6074 ms`, fill batch `4.3713 ms`, post spawn `2.7404 ms`, presenter batch `20.9475 ms`, presenter create `20.5016 ms`, presenter setup `0.7170 ms`, presenter world create `9.8648 ms`, presenter component fill `6.1393 ms`, presenter index write `1.0864 ms`, presenter owner payload `0.1352 ms`, presenter post create `1.1439 ms`, bootstrap mark `0.0000 ms`, first tick `298.5338 ms`, validation scans `0.5568 ms`, init diag transform sync `0.0098 ms`, init diag emit `16.2182 ms`, dirty emit process `16.1714 ms`, dirty emit cleanup `0.0000 ms`, init diag request flush `7.6978 ms`, init diag culling `94.5516 ms`, init cull entity `91.2768 ms`, init cull static `48.7989 ms`, init cull pending remove `0.0000 ms`, init cull dynamic `0.0000 ms`, init cull presenter sync `3.2686 ms`, initial bridge sync per primitive `0.000967 ms`, stable avg tick per entity `0.000318 ms`, drops events `0` commands `0` primitives `0`
- 30000: init create+emit per entity `0.054974 ms`, runtime prepare `3.0789 ms`, world create `22.9129 ms`, fill batch `18.5155 ms`, post spawn `10.7623 ms`, presenter batch `72.8890 ms`, presenter create `71.0444 ms`, presenter setup `2.6325 ms`, presenter world create `32.8714 ms`, presenter component fill `20.2561 ms`, presenter index write `4.9518 ms`, presenter owner payload `0.5201 ms`, presenter post create `4.4931 ms`, bootstrap mark `0.0000 ms`, first tick `1647.6047 ms`, validation scans `1.6247 ms`, init diag transform sync `0.0122 ms`, init diag emit `45.7009 ms`, dirty emit process `45.6389 ms`, dirty emit cleanup `0.0000 ms`, init diag request flush `8.9921 ms`, init diag culling `688.7353 ms`, init cull entity `679.2253 ms`, init cull static `511.1488 ms`, init cull pending remove `0.0000 ms`, init cull dynamic `0.0000 ms`, init cull presenter sync `9.5033 ms`, initial bridge sync per primitive `0.000704 ms`, stable avg tick per entity `0.000357 ms`, drops events `0` commands `0` primitives `0`
