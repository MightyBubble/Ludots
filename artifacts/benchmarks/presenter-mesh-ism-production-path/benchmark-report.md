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
| 3000 | 1.3014 ms | 124.2732 ms | 121.2360 ms | 2.4266 ms | 7.4647 ms | 1 | 3000 | 3000 | 3000 | 1 |
| 10000 | 3.1001 ms | 373.4428 ms | 372.3563 ms | 1.0796 ms | 14.0622 ms | 1 | 10000 | 10000 | 10000 | 1 |
| 30000 | 15.2249 ms | 2193.8856 ms | 2191.1659 ms | 2.7141 ms | 33.1566 ms | 1 | 30000 | 30000 | 30000 | 1 |

## Init Breakdown

| Count | init diag Total Tick | init diag Presentation | init diag Simulation | runtime batch | runtime prepare | runtime world create | runtime fill batch | runtime post spawn | runtime presenter batch | runtime presenter create | presenter setup | presenter world create | presenter component fill | presenter index write | presenter owner payload | presenter post create | runtime bootstrap mark | runtime presenters | init diag Camera Culling | init cull entity | init cull static | init cull pending remove | init cull dynamic | init cull presenter sync | init diag Behavior | init diag Animator | init diag Transform Sync | init diag Emit | init diag Emit Process | init diag Emit Cleanup | init dirty presenters | init diag Request Flush | init top presentation systems | init top simulation systems |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| 3000 | 121.2110 ms | 57.8376 ms | 63.3128 ms | 3000 | 0.3867 ms | 0.6972 ms | 1.8024 ms | 0.5033 ms | 4.9537 ms | 4.7967 ms | 0.3356 ms | 0.5594 ms | 2.4554 ms | 0.5018 ms | 0.0501 ms | 0.3917 ms | 0.0000 ms | 3000 | 28.6479 ms | 27.4503 ms | 12.8344 ms | 0.0000 ms | 0.0000 ms | 1.1909 ms | 0.0236 ms | 0.0038 ms | 0.0112 ms | 6.0546 ms | 6.0358 ms | 0.0000 ms | 3000 | 0.9162 ms | CameraCullingSystem 28.6495 ms; WorldToVisualSyncSystem 20.6341 ms; PresenterEmitSystem 6.0556 ms | MapEntityLifecycleObserverSystem 52.1922 ms; RuntimeEntitySpawnSystem 10.1704 ms; PresenterBlacksmithShowcaseKnowledgeProjectionSystem 0.1975 ms |
| 10000 | 372.3234 ms | 277.0403 ms | 95.2251 ms | 10000 | 0.6868 ms | 18.4533 ms | 5.2692 ms | 1.4052 ms | 21.4862 ms | 21.1179 ms | 0.9865 ms | 10.1036 ms | 6.3409 ms | 1.0537 ms | 0.1421 ms | 1.0929 ms | 0.0000 ms | 10000 | 142.3996 ms | 137.2229 ms | 72.7161 ms | 0.0000 ms | 0.0000 ms | 5.1703 ms | 0.0363 ms | 0.0047 ms | 0.0141 ms | 26.1874 ms | 26.1404 ms | 0.0000 ms | 10000 | 16.6452 ms | CameraCullingSystem 142.4010 ms; WorldToVisualSyncSystem 85.6871 ms; PresenterEmitSystem 26.1893 ms | RuntimeEntitySpawnSystem 53.4643 ms; MapEntityLifecycleObserverSystem 40.4246 ms; PresenterBlacksmithShowcaseKnowledgeProjectionSystem 0.6341 ms |
| 30000 | 2191.1360 ms | 1932.8354 ms | 258.1644 ms | 30000 | 3.3690 ms | 28.9819 ms | 20.8909 ms | 5.1334 ms | 86.7174 ms | 85.1108 ms | 3.0881 ms | 40.9485 ms | 24.8143 ms | 5.7125 ms | 0.7850 ms | 4.2012 ms | 0.0000 ms | 30000 | 940.5636 ms | 923.6694 ms | 665.9218 ms | 0.0000 ms | 0.0000 ms | 16.8868 ms | 0.0365 ms | 0.0036 ms | 0.0125 ms | 72.4228 ms | 72.3781 ms | 0.0000 ms | 30000 | 8.9688 ms | CameraCullingSystem 940.5654 ms; WorldToVisualSyncSystem 890.2406 ms; PresenterEmitSystem 72.4251 ms | RuntimeEntitySpawnSystem 195.2068 ms; MapEntityLifecycleObserverSystem 58.7144 ms; PresenterBlacksmithShowcaseKnowledgeProjectionSystem 3.2671 ms |

## Stable Tick

| Count | Frames | Avg Tick | P95 Tick | Max Tick | Avg Bridge Sync | P95 Bridge Sync | Max Bridge Sync |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 3000 | 60 | 0.4579 ms | 1.2289 ms | 1.6183 ms | 0.0003 ms | 0.0008 ms | 0.0012 ms |
| 10000 | 60 | 1.8329 ms | 4.3439 ms | 5.1664 ms | 0.0008 ms | 0.0015 ms | 0.0018 ms |
| 30000 | 60 | 5.7106 ms | 14.2847 ms | 22.7934 ms | 0.0024 ms | 0.0037 ms | 0.0038 ms |

## Tick Breakdown

> `diag_*` values below come from `PresentationTimingDiagnostics` and are exponentially smoothed in-engine; use them as stable attribution, not exact per-frame wall-clock sums.

| Count | diag Total Tick | diag Presentation | diag Simulation | diag Camera Culling | diag cull entity | diag cull presenter sync | diag Behavior | diag Animator | diag Transform Sync | diag Emit | diag Request Flush |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 3000 | 0.8013 ms | 0.3479 ms | 0.4248 ms | 0.1040 ms | 0.0993 ms | 0.0034 ms | 0.0042 ms | 0.0004 ms | 0.0011 ms | 0.0207 ms | 0.0028 ms |
| 10000 | 2.8317 ms | 1.5356 ms | 1.2370 ms | 0.4208 ms | 0.4030 ms | 0.0147 ms | 0.0097 ms | 0.0011 ms | 0.0029 ms | 0.0807 ms | 0.0472 ms |
| 30000 | 11.6804 ms | 7.8486 ms | 3.7326 ms | 2.6740 ms | 2.6217 ms | 0.0480 ms | 0.0338 ms | 0.0013 ms | 0.0067 ms | 0.2145 ms | 0.0265 ms |

- 3000: init create+emit per entity `0.041424 ms`, runtime prepare `0.3867 ms`, world create `0.6972 ms`, fill batch `1.8024 ms`, post spawn `0.5033 ms`, presenter batch `4.9537 ms`, presenter create `4.7967 ms`, presenter setup `0.3356 ms`, presenter world create `0.5594 ms`, presenter component fill `2.4554 ms`, presenter index write `0.5018 ms`, presenter owner payload `0.0501 ms`, presenter post create `0.3917 ms`, bootstrap mark `0.0000 ms`, first tick `121.2360 ms`, validation scans `2.4266 ms`, init diag transform sync `0.0112 ms`, init diag emit `6.0546 ms`, dirty emit process `6.0358 ms`, dirty emit cleanup `0.0000 ms`, init diag request flush `0.9162 ms`, init diag culling `28.6479 ms`, init cull entity `27.4503 ms`, init cull static `12.8344 ms`, init cull pending remove `0.0000 ms`, init cull dynamic `0.0000 ms`, init cull presenter sync `1.1909 ms`, initial bridge sync per primitive `0.002488 ms`, stable avg tick per entity `0.000153 ms`, drops events `0` commands `0` primitives `0`
- 10000: init create+emit per entity `0.037344 ms`, runtime prepare `0.6868 ms`, world create `18.4533 ms`, fill batch `5.2692 ms`, post spawn `1.4052 ms`, presenter batch `21.4862 ms`, presenter create `21.1179 ms`, presenter setup `0.9865 ms`, presenter world create `10.1036 ms`, presenter component fill `6.3409 ms`, presenter index write `1.0537 ms`, presenter owner payload `0.1421 ms`, presenter post create `1.0929 ms`, bootstrap mark `0.0000 ms`, first tick `372.3563 ms`, validation scans `1.0796 ms`, init diag transform sync `0.0141 ms`, init diag emit `26.1874 ms`, dirty emit process `26.1404 ms`, dirty emit cleanup `0.0000 ms`, init diag request flush `16.6452 ms`, init diag culling `142.3996 ms`, init cull entity `137.2229 ms`, init cull static `72.7161 ms`, init cull pending remove `0.0000 ms`, init cull dynamic `0.0000 ms`, init cull presenter sync `5.1703 ms`, initial bridge sync per primitive `0.001406 ms`, stable avg tick per entity `0.000183 ms`, drops events `0` commands `0` primitives `0`
- 30000: init create+emit per entity `0.073130 ms`, runtime prepare `3.3690 ms`, world create `28.9819 ms`, fill batch `20.8909 ms`, post spawn `5.1334 ms`, presenter batch `86.7174 ms`, presenter create `85.1108 ms`, presenter setup `3.0881 ms`, presenter world create `40.9485 ms`, presenter component fill `24.8143 ms`, presenter index write `5.7125 ms`, presenter owner payload `0.7850 ms`, presenter post create `4.2012 ms`, bootstrap mark `0.0000 ms`, first tick `2191.1659 ms`, validation scans `2.7141 ms`, init diag transform sync `0.0125 ms`, init diag emit `72.4228 ms`, dirty emit process `72.3781 ms`, dirty emit cleanup `0.0000 ms`, init diag request flush `8.9688 ms`, init diag culling `940.5636 ms`, init cull entity `923.6694 ms`, init cull static `665.9218 ms`, init cull pending remove `0.0000 ms`, init cull dynamic `0.0000 ms`, init cull presenter sync `16.8868 ms`, initial bridge sync per primitive `0.001105 ms`, stable avg tick per entity `0.000190 ms`, drops events `0` commands `0` primitives `0`
