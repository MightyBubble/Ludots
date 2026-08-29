# Presenter Dynamic Worker Production Path Benchmark

- template: `blacksmith_dynamic_worker_entity`
- presenter rule: `EntitySpawned -> blacksmith_dynamic_worker_actor`
- render path: `SkinnedMesh` through production `PresentationRequest`/`StableDrawCache`/`SkinnedVisualBatchBuffer`
- animator: `blacksmith.worker.locomotion` packed state
- grounding: presenter `Grounding` behavior, batched through `PresenterGroundingUtility.ResolveBatch`
- attachment: child presenter `blacksmith_dynamic_worker_tool_attachment` follows the worker through `Attachment` behavior
- movement: mod-owned ECS `DynamicWorkerCrowdMovementSystem`, no fake render data

## Init

| Count | Enqueue | First Tick | init Total | init Sim | init Pres | init Culling | init Transform Sync | init Behavior | init Animator | init Emit | init Dirty Emit | dirty count | retained emit | retained count | init Flush | top presentation | top simulation |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| 3000 | 0.0000 ms | 319.2551 ms | 316.2960 ms | 240.2455 ms | 76.0195 ms | 2.0018 ms | 10.2820 ms | 25.8768 ms | 1.3033 ms | 0.2959 ms | 0.0000 ms | 0 | 0.0000 ms | 0 | 0.0038 ms | PresentationEntityLifecycleSystem 30.6537 ms | RuntimeEntitySpawnSystem 237.0565 ms |
| 10000 | 0.0000 ms | 1108.8034 ms | 1107.4774 ms | 845.0832 ms | 262.3726 ms | 7.4492 ms | 32.7236 ms | 117.4843 ms | 5.3062 ms | 1.0118 ms | 0.0000 ms | 0 | 0.0000 ms | 0 | 0.0020 ms | PresenterBehaviorSystem 117.4923 ms | RuntimeEntitySpawnSystem 843.0506 ms |
| 30000 | 0.0000 ms | 4048.4950 ms | 4046.3208 ms | 2374.7808 ms | 1671.5199 ms | 21.3524 ms | 112.1800 ms | 767.8130 ms | 17.2542 ms | 9.6546 ms | 0.0000 ms | 0 | 0.0000 ms | 0 | 0.0027 ms | PresenterBehaviorSystem 767.8170 ms | RuntimeEntitySpawnSystem 2368.2878 ms |

## Stable Tick

| Count | Entities | Root Presenters | Attach Presenters | Skinned | Walking State | Animators | Grounded | Attached | Moved | Avg Tick | P95 Tick | Max Tick | Avg FPS | Avg Sim | Avg Pres | Avg Culling | Avg Transform Sync | Avg Behavior | Avg Animator | Avg Emit | Avg Dirty Emit | Avg Retained Emit | Avg Flush | top presentation | top simulation |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| 3000 | 3000 | 3000 | 3000 | 25 | 25 | 3000 | 3000 | 3000 | 3000 | 17.8818 ms | 21.4637 ms | 100.7248 ms | 55.9 | 1.4238 ms | 16.3352 ms | 0.3636 ms | 10.4128 ms | 2.5905 ms | 0.9897 ms | 0.3142 ms | 0.0000 ms | 0.0000 ms | 0.0023 ms | PresenterEntityTransformSyncSystem 10.4144 ms | DynamicWorkerCrowdMovementSystem 0.3989 ms |
| 10000 | 10000 | 10000 | 10000 | 64 | 64 | 10000 | 10000 | 10000 | 10000 | 58.4717 ms | 65.7770 ms | 157.6629 ms | 17.1 | 3.3861 ms | 54.9489 ms | 0.9801 ms | 34.9137 ms | 9.1847 ms | 3.5908 ms | 1.1571 ms | 0.0000 ms | 0.0000 ms | 0.0028 ms | PresenterEntityTransformSyncSystem 34.9154 ms | DynamicWorkerCrowdMovementSystem 1.2637 ms |
| 30000 | 30000 | 30000 | 30000 | 214 | 214 | 30000 | 30000 | 30000 | 30000 | 169.6584 ms | 217.4566 ms | 390.6718 ms | 5.9 | 6.1033 ms | 163.4273 ms | 2.7442 ms | 103.7873 ms | 28.0703 ms | 10.2831 ms | 3.7989 ms | 0.0000 ms | 0.0000 ms | 0.0025 ms | PresenterEntityTransformSyncSystem 103.7894 ms | DynamicWorkerCrowdMovementSystem 3.9106 ms |

- 3000: avg dirty emit count `0.0`, avg retained emit count `0.0`, avg skinned count `25.0`, gpu skinned `25`, direct skinned frames `90/90`, drops events `0` commands `0` skinned `0`
- 10000: avg dirty emit count `0.0`, avg retained emit count `0.0`, avg skinned count `64.0`, gpu skinned `64`, direct skinned frames `90/90`, drops events `0` commands `0` skinned `0`
- 30000: avg dirty emit count `0.0`, avg retained emit count `0.0`, avg skinned count `214.2`, gpu skinned `214`, direct skinned frames `90/90`, drops events `0` commands `0` skinned `0`
