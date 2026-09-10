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
| 3000 | 0.6909 ms | 83.7557 ms | 82.3566 ms | 0.9409 ms | 7.9422 ms | 1 | 3000 | 3000 | 3000 | 1 |
| 10000 | 4.3032 ms | 188.8980 ms | 185.5120 ms | 3.3789 ms | 18.2146 ms | 1 | 10000 | 10000 | 10000 | 1 |
| 30000 | 7.0482 ms | 1036.6991 ms | 1035.3094 ms | 1.3827 ms | 15.2792 ms | 1 | 30000 | 30000 | 30000 | 1 |

## Init Breakdown

| Count | init diag Total Tick | init diag Presentation | init diag Simulation | runtime batch | runtime prepare | runtime world create | runtime fill batch | runtime post spawn | runtime presenter batch | runtime presenter create | presenter setup | presenter world create | presenter component fill | presenter index write | presenter owner payload | presenter post create | runtime bootstrap mark | runtime presenters | init diag Camera Culling | init cull entity | init cull static | init cull pending remove | init cull dynamic | init cull presenter sync | init diag Behavior | init diag Animator | init diag Transform Sync | init diag Emit | init diag Emit Process | init diag Emit Cleanup | init dirty presenters | init diag Request Flush | init top presentation systems | init top simulation systems |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| 3000 | 82.3485 ms | 20.7596 ms | 61.5624 ms | 3000 | 0.1274 ms | 0.4310 ms | 0.7002 ms | 0.1618 ms | 18.1209 ms | 18.0129 ms | 0.1624 ms | 15.7468 ms | 0.7324 ms | 0.2752 ms | 0.0408 ms | 0.9185 ms | 0.0000 ms | 3000 | 8.1585 ms | 8.0213 ms | 4.7102 ms | 0.0000 ms | 0.0000 ms | 0.1358 ms | 0.0075 ms | 0.0012 ms | 0.0021 ms | 1.7726 ms | 1.7563 ms | 0.0000 ms | 3000 | 0.4937 ms | WorldToVisualSyncSystem 10.0119 ms; CameraCullingSystem 8.1599 ms; PresenterEmitSystem 1.7735 ms | MapEntityLifecycleObserverSystem 39.9957 ms; RuntimeEntitySpawnSystem 21.0351 ms; PresenterBlacksmithShowcaseKnowledgeProjectionSystem 0.1645 ms |
| 10000 | 185.5020 ms | 95.6088 ms | 89.8484 ms | 10000 | 0.3303 ms | 2.4831 ms | 2.9638 ms | 0.3162 ms | 17.3042 ms | 17.1778 ms | 0.2480 ms | 11.2753 ms | 2.1337 ms | 0.8731 ms | 0.0994 ms | 2.2792 ms | 0.0000 ms | 10000 | 34.4554 ms | 34.1124 ms | 23.5077 ms | 0.0000 ms | 0.0000 ms | 0.3412 ms | 0.0071 ms | 0.0012 ms | 0.0015 ms | 3.9397 ms | 3.9256 ms | 0.0000 ms | 10000 | 1.0866 ms | WorldToVisualSyncSystem 55.6541 ms; CameraCullingSystem 34.4570 ms; PresenterEmitSystem 3.9401 ms | RuntimeEntitySpawnSystem 58.9266 ms; MapEntityLifecycleObserverSystem 30.2961 ms; PresenterBlacksmithShowcaseKnowledgeProjectionSystem 0.2239 ms |
| 30000 | 1035.2982 ms | 902.7910 ms | 132.4459 ms | 30000 | 0.8347 ms | 20.6422 ms | 6.3607 ms | 0.9674 ms | 30.1700 ms | 29.7703 ms | 0.5637 ms | 17.1800 ms | 5.1985 ms | 1.9116 ms | 0.2896 ms | 4.1131 ms | 0.0000 ms | 30000 | 448.2325 ms | 446.4960 ms | 354.5124 ms | 0.0000 ms | 0.0000 ms | 1.7344 ms | 0.0107 ms | 0.0011 ms | 0.0033 ms | 17.2809 ms | 17.2581 ms | 0.0000 ms | 30000 | 4.6791 ms | CameraCullingSystem 448.2342 ms; WorldToVisualSyncSystem 430.5566 ms; PresenterEmitSystem 17.2818 ms | RuntimeEntitySpawnSystem 106.1967 ms; MapEntityLifecycleObserverSystem 25.3291 ms; PresenterBlacksmithShowcaseKnowledgeProjectionSystem 0.5226 ms |

## Stable Tick

| Count | Frames | Avg Tick | P95 Tick | Max Tick | Avg Bridge Sync | P95 Bridge Sync | Max Bridge Sync |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 3000 | 60 | 0.5795 ms | 0.8888 ms | 0.9103 ms | 0.0017 ms | 0.0113 ms | 0.0174 ms |
| 10000 | 60 | 1.6575 ms | 3.0466 ms | 4.0340 ms | 0.0002 ms | 0.0004 ms | 0.0006 ms |
| 30000 | 60 | 7.7694 ms | 10.9296 ms | 11.7492 ms | 0.0010 ms | 0.0029 ms | 0.0031 ms |

## Tick Breakdown

> `diag_*` values below come from `PresentationTimingDiagnostics` and are exponentially smoothed in-engine; use them as stable attribution, not exact per-frame wall-clock sums.

| Count | diag Total Tick | diag Presentation | diag Simulation | diag Camera Culling | diag cull entity | diag cull presenter sync | diag Behavior | diag Animator | diag Transform Sync | diag Emit | diag Request Flush |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 3000 | 0.9436 ms | 0.6829 ms | 0.2801 ms | 0.0257 ms | 0.0250 ms | 0.0004 ms | 0.0016 ms | 0.0002 ms | 0.0003 ms | 0.0068 ms | 0.4258 ms |
| 10000 | 2.3055 ms | 1.8138 ms | 0.5168 ms | 0.1009 ms | 0.0994 ms | 0.0010 ms | 0.0026 ms | 0.0001 ms | 0.0006 ms | 0.0140 ms | 1.3020 ms |
| 30000 | 10.6665 ms | 9.3756 ms | 1.2595 ms | 1.2681 ms | 1.2613 ms | 0.0054 ms | 0.0065 ms | 0.0010 ms | 0.0015 ms | 0.0630 ms | 5.9836 ms |

- 3000: init create+emit per entity `0.027919 ms`, runtime prepare `0.1274 ms`, world create `0.4310 ms`, fill batch `0.7002 ms`, post spawn `0.1618 ms`, presenter batch `18.1209 ms`, presenter create `18.0129 ms`, presenter setup `0.1624 ms`, presenter world create `15.7468 ms`, presenter component fill `0.7324 ms`, presenter index write `0.2752 ms`, presenter owner payload `0.0408 ms`, presenter post create `0.9185 ms`, bootstrap mark `0.0000 ms`, first tick `82.3566 ms`, validation scans `0.9409 ms`, init diag transform sync `0.0021 ms`, init diag emit `1.7726 ms`, dirty emit process `1.7563 ms`, dirty emit cleanup `0.0000 ms`, init diag request flush `0.4937 ms`, init diag culling `8.1585 ms`, init cull entity `8.0213 ms`, init cull static `4.7102 ms`, init cull pending remove `0.0000 ms`, init cull dynamic `0.0000 ms`, init cull presenter sync `0.1358 ms`, initial bridge sync per primitive `0.002647 ms`, stable avg tick per entity `0.000193 ms`, drops events `0` commands `0` primitives `0`
- 10000: init create+emit per entity `0.018890 ms`, runtime prepare `0.3303 ms`, world create `2.4831 ms`, fill batch `2.9638 ms`, post spawn `0.3162 ms`, presenter batch `17.3042 ms`, presenter create `17.1778 ms`, presenter setup `0.2480 ms`, presenter world create `11.2753 ms`, presenter component fill `2.1337 ms`, presenter index write `0.8731 ms`, presenter owner payload `0.0994 ms`, presenter post create `2.2792 ms`, bootstrap mark `0.0000 ms`, first tick `185.5120 ms`, validation scans `3.3789 ms`, init diag transform sync `0.0015 ms`, init diag emit `3.9397 ms`, dirty emit process `3.9256 ms`, dirty emit cleanup `0.0000 ms`, init diag request flush `1.0866 ms`, init diag culling `34.4554 ms`, init cull entity `34.1124 ms`, init cull static `23.5077 ms`, init cull pending remove `0.0000 ms`, init cull dynamic `0.0000 ms`, init cull presenter sync `0.3412 ms`, initial bridge sync per primitive `0.001821 ms`, stable avg tick per entity `0.000166 ms`, drops events `0` commands `0` primitives `0`
- 30000: init create+emit per entity `0.034557 ms`, runtime prepare `0.8347 ms`, world create `20.6422 ms`, fill batch `6.3607 ms`, post spawn `0.9674 ms`, presenter batch `30.1700 ms`, presenter create `29.7703 ms`, presenter setup `0.5637 ms`, presenter world create `17.1800 ms`, presenter component fill `5.1985 ms`, presenter index write `1.9116 ms`, presenter owner payload `0.2896 ms`, presenter post create `4.1131 ms`, bootstrap mark `0.0000 ms`, first tick `1035.3094 ms`, validation scans `1.3827 ms`, init diag transform sync `0.0033 ms`, init diag emit `17.2809 ms`, dirty emit process `17.2581 ms`, dirty emit cleanup `0.0000 ms`, init diag request flush `4.6791 ms`, init diag culling `448.2325 ms`, init cull entity `446.4960 ms`, init cull static `354.5124 ms`, init cull pending remove `0.0000 ms`, init cull dynamic `0.0000 ms`, init cull presenter sync `1.7344 ms`, initial bridge sync per primitive `0.000509 ms`, stable avg tick per entity `0.000259 ms`, drops events `0` commands `0` primitives `0`
