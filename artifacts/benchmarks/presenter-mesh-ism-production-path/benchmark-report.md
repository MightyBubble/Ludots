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
| 3000 | 0.4930 ms | 59.3988 ms | 58.2674 ms | 0.6814 ms | 4.7747 ms | 1 | 3000 | 3000 | 3000 | 1 |
| 10000 | 1.6170 ms | 186.3550 ms | 185.8536 ms | 0.4976 ms | 5.5406 ms | 1 | 10000 | 10000 | 10000 | 1 |
| 30000 | 7.5503 ms | 960.6125 ms | 959.3334 ms | 1.2755 ms | 26.9487 ms | 1 | 30000 | 30000 | 30000 | 1 |

## Init Breakdown

| Count | init diag Total Tick | init diag Presentation | init diag Simulation | runtime batch | runtime prepare | runtime world create | runtime fill batch | runtime post spawn | runtime presenter batch | runtime presenter create | presenter setup | presenter world create | presenter component fill | presenter index write | presenter owner payload | presenter post create | runtime bootstrap mark | runtime presenters | init diag Camera Culling | init cull entity | init cull static | init cull pending remove | init cull dynamic | init cull presenter sync | init diag Behavior | init diag Animator | init diag Transform Sync | init diag Emit | init diag Emit Process | init diag Emit Cleanup | init dirty presenters | init diag Request Flush | init top presentation systems | init top simulation systems |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| 3000 | 58.2448 ms | 42.2560 ms | 15.9612 ms | 3000 | 0.1655 ms | 0.4331 ms | 0.9994 ms | 0.3187 ms | 12.0477 ms | 11.9614 ms | 0.1700 ms | 10.2505 ms | 0.8017 ms | 0.2228 ms | 0.0326 ms | 0.2118 ms | 0.0000 ms | 3000 | 20.0940 ms | 19.3695 ms | 8.2373 ms | 0.0000 ms | 0.0000 ms | 0.7191 ms | 0.0162 ms | 0.0023 ms | 0.0072 ms | 3.6482 ms | 3.6058 ms | 0.0000 ms | 3000 | 0.6054 ms | CameraCullingSystem 20.0951 ms; WorldToVisualSyncSystem 16.8816 ms; PresenterEmitSystem 3.6486 ms | RuntimeEntitySpawnSystem 15.3800 ms; PresenterBlacksmithShowcaseKnowledgeProjectionSystem 0.1343 ms; ClearPresentationFlagsSystem 0.0468 ms |
| 10000 | 185.8297 ms | 140.4617 ms | 45.3334 ms | 10000 | 1.3056 ms | 19.6204 ms | 2.9423 ms | 1.0755 ms | 14.9739 ms | 14.6542 ms | 0.4895 ms | 8.7891 ms | 3.2440 ms | 0.6409 ms | 0.0858 ms | 0.7005 ms | 0.0000 ms | 10000 | 69.1877 ms | 66.9287 ms | 38.7455 ms | 0.0000 ms | 0.0000 ms | 2.2530 ms | 0.0189 ms | 0.0020 ms | 0.0063 ms | 11.5200 ms | 11.4676 ms | 0.0000 ms | 10000 | 2.2767 ms | CameraCullingSystem 69.1890 ms; WorldToVisualSyncSystem 55.4090 ms; PresenterEmitSystem 11.5212 ms | RuntimeEntitySpawnSystem 44.5669 ms; PresenterBlacksmithShowcaseKnowledgeProjectionSystem 0.2688 ms; Physics2DSimulationSystem 0.0571 ms |
| 30000 | 959.3110 ms | 847.8780 ms | 111.3445 ms | 30000 | 1.9681 ms | 17.9060 ms | 10.7658 ms | 2.2864 ms | 45.4009 ms | 44.8592 ms | 1.3326 ms | 26.0868 ms | 10.0145 ms | 2.6537 ms | 0.3128 ms | 1.8217 ms | 0.0000 ms | 30000 | 420.1380 ms | 414.1585 ms | 304.8004 ms | 0.0000 ms | 0.0000 ms | 5.9739 ms | 0.0202 ms | 0.0016 ms | 0.0062 ms | 34.0944 ms | 34.0456 ms | 0.0000 ms | 30000 | 6.8427 ms | CameraCullingSystem 420.1389 ms; WorldToVisualSyncSystem 380.3784 ms; PresenterEmitSystem 34.0952 ms | RuntimeEntitySpawnSystem 109.2153 ms; PresenterBlacksmithShowcaseKnowledgeProjectionSystem 1.4868 ms; Physics2DSimulationSystem 0.0520 ms |

## Stable Tick

| Count | Frames | Avg Tick | P95 Tick | Max Tick | Avg Bridge Sync | P95 Bridge Sync | Max Bridge Sync |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 3000 | 60 | 0.7439 ms | 1.1156 ms | 1.7322 ms | 0.0002 ms | 0.0006 ms | 0.0015 ms |
| 10000 | 60 | 1.8238 ms | 2.6967 ms | 3.0583 ms | 0.0006 ms | 0.0011 ms | 0.0012 ms |
| 30000 | 60 | 7.7402 ms | 11.7860 ms | 13.9498 ms | 0.0025 ms | 0.0037 ms | 0.0082 ms |

## Tick Breakdown

> `diag_*` values below come from `PresentationTimingDiagnostics` and are exponentially smoothed in-engine; use them as stable attribution, not exact per-frame wall-clock sums.

| Count | diag Total Tick | diag Presentation | diag Simulation | diag Camera Culling | diag cull entity | diag cull presenter sync | diag Behavior | diag Animator | diag Transform Sync | diag Emit | diag Request Flush |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 3000 | 1.1274 ms | 0.7356 ms | 0.3721 ms | 0.0635 ms | 0.0605 ms | 0.0021 ms | 0.0029 ms | 0.0003 ms | 0.0006 ms | 0.0167 ms | 0.4903 ms |
| 10000 | 3.0181 ms | 1.9002 ms | 1.0850 ms | 0.2061 ms | 0.1978 ms | 0.0064 ms | 0.0059 ms | 0.0004 ms | 0.0015 ms | 0.0459 ms | 1.2210 ms |
| 30000 | 12.4007 ms | 9.3132 ms | 3.0089 ms | 1.2042 ms | 1.1835 ms | 0.0172 ms | 0.0118 ms | 0.0007 ms | 0.0039 ms | 0.1228 ms | 5.8338 ms |

- 3000: init create+emit per entity `0.019800 ms`, runtime prepare `0.1655 ms`, world create `0.4331 ms`, fill batch `0.9994 ms`, post spawn `0.3187 ms`, presenter batch `12.0477 ms`, presenter create `11.9614 ms`, presenter setup `0.1700 ms`, presenter world create `10.2505 ms`, presenter component fill `0.8017 ms`, presenter index write `0.2228 ms`, presenter owner payload `0.0326 ms`, presenter post create `0.2118 ms`, bootstrap mark `0.0000 ms`, first tick `58.2674 ms`, validation scans `0.6814 ms`, init diag transform sync `0.0072 ms`, init diag emit `3.6482 ms`, dirty emit process `3.6058 ms`, dirty emit cleanup `0.0000 ms`, init diag request flush `0.6054 ms`, init diag culling `20.0940 ms`, init cull entity `19.3695 ms`, init cull static `8.2373 ms`, init cull pending remove `0.0000 ms`, init cull dynamic `0.0000 ms`, init cull presenter sync `0.7191 ms`, initial bridge sync per primitive `0.001592 ms`, stable avg tick per entity `0.000248 ms`, drops events `0` commands `0` primitives `0`
- 10000: init create+emit per entity `0.018635 ms`, runtime prepare `1.3056 ms`, world create `19.6204 ms`, fill batch `2.9423 ms`, post spawn `1.0755 ms`, presenter batch `14.9739 ms`, presenter create `14.6542 ms`, presenter setup `0.4895 ms`, presenter world create `8.7891 ms`, presenter component fill `3.2440 ms`, presenter index write `0.6409 ms`, presenter owner payload `0.0858 ms`, presenter post create `0.7005 ms`, bootstrap mark `0.0000 ms`, first tick `185.8536 ms`, validation scans `0.4976 ms`, init diag transform sync `0.0063 ms`, init diag emit `11.5200 ms`, dirty emit process `11.4676 ms`, dirty emit cleanup `0.0000 ms`, init diag request flush `2.2767 ms`, init diag culling `69.1877 ms`, init cull entity `66.9287 ms`, init cull static `38.7455 ms`, init cull pending remove `0.0000 ms`, init cull dynamic `0.0000 ms`, init cull presenter sync `2.2530 ms`, initial bridge sync per primitive `0.000554 ms`, stable avg tick per entity `0.000182 ms`, drops events `0` commands `0` primitives `0`
- 30000: init create+emit per entity `0.032020 ms`, runtime prepare `1.9681 ms`, world create `17.9060 ms`, fill batch `10.7658 ms`, post spawn `2.2864 ms`, presenter batch `45.4009 ms`, presenter create `44.8592 ms`, presenter setup `1.3326 ms`, presenter world create `26.0868 ms`, presenter component fill `10.0145 ms`, presenter index write `2.6537 ms`, presenter owner payload `0.3128 ms`, presenter post create `1.8217 ms`, bootstrap mark `0.0000 ms`, first tick `959.3334 ms`, validation scans `1.2755 ms`, init diag transform sync `0.0062 ms`, init diag emit `34.0944 ms`, dirty emit process `34.0456 ms`, dirty emit cleanup `0.0000 ms`, init diag request flush `6.8427 ms`, init diag culling `420.1380 ms`, init cull entity `414.1585 ms`, init cull static `304.8004 ms`, init cull pending remove `0.0000 ms`, init cull dynamic `0.0000 ms`, init cull presenter sync `5.9739 ms`, initial bridge sync per primitive `0.000898 ms`, stable avg tick per entity `0.000258 ms`, drops events `0` commands `0` primitives `0`
