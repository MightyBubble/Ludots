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
| 3000 | 0.0000 ms | 196.5651 ms | 194.8338 ms | 152.4713 ms | 42.3522 ms | 1.3732 ms | 5.7779 ms | 14.7856 ms | 0.7712 ms | 0.1597 ms | 0.0000 ms | 0 | 0.0000 ms | 0 | 0.0026 ms | PresentationEntityLifecycleSystem 16.2704 ms | RuntimeEntitySpawnSystem 150.7374 ms |
| 10000 | 0.0000 ms | 640.3952 ms | 639.6352 ms | 454.1805 ms | 185.4446 ms | 4.7667 ms | 23.8104 ms | 65.3024 ms | 2.5122 ms | 0.5988 ms | 0.0000 ms | 0 | 0.0000 ms | 0 | 0.0022 ms | PresentationEntityLifecycleSystem 74.9122 ms | RuntimeEntitySpawnSystem 452.5946 ms |
| 30000 | 0.0000 ms | 2178.9023 ms | 2177.5745 ms | 1365.5101 ms | 812.0544 ms | 12.6986 ms | 56.2128 ms | 347.0721 ms | 7.4480 ms | 1.8858 ms | 0.0000 ms | 0 | 0.0000 ms | 0 | 0.0021 ms | PresentationEntityLifecycleSystem 358.5185 ms | RuntimeEntitySpawnSystem 1362.0612 ms |

## Stable Tick

| Count | Entities | Root Presenters | Attach Presenters | Skinned | Walking State | Animators | Grounded | Attached | Moved | Avg Tick | P95 Tick | Max Tick | Avg FPS | Avg Sim | Avg Pres | Avg Culling | Avg Transform Sync | Avg Behavior | Avg Animator | Avg Emit | Avg Dirty Emit | Avg Retained Emit | Avg Flush | top presentation | top simulation |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| 3000 | 3000 | 3000 | 3000 | 25 | 25 | 3000 | 3000 | 3000 | 3000 | 10.1749 ms | 12.7828 ms | 52.6831 ms | 98.3 | 0.8182 ms | 9.2723 ms | 0.2078 ms | 5.8534 ms | 1.4733 ms | 0.5844 ms | 0.1708 ms | 0.0000 ms | 0.0000 ms | 0.0021 ms | PresenterEntityTransformSyncSystem 5.8542 ms | DynamicWorkerCrowdMovementSystem 0.2696 ms |
| 10000 | 10000 | 10000 | 10000 | 64 | 64 | 10000 | 10000 | 10000 | 10000 | 31.8191 ms | 33.2260 ms | 94.1980 ms | 31.4 | 1.7837 ms | 29.9443 ms | 0.5256 ms | 19.0302 ms | 5.0433 ms | 1.9117 ms | 0.5602 ms | 0.0000 ms | 0.0000 ms | 0.0020 ms | PresenterEntityTransformSyncSystem 19.0316 ms | DynamicWorkerCrowdMovementSystem 0.8383 ms |
| 30000 | 30000 | 30000 | 30000 | 214 | 214 | 30000 | 30000 | 30000 | 30000 | 103.7932 ms | 130.5857 ms | 319.6182 ms | 9.6 | 4.4494 ms | 99.2467 ms | 1.5840 ms | 63.1845 ms | 17.1902 ms | 6.2856 ms | 2.2421 ms | 0.0000 ms | 0.0000 ms | 0.0023 ms | PresenterEntityTransformSyncSystem 63.1864 ms | DynamicWorkerCrowdMovementSystem 2.5073 ms |

- 3000: avg dirty emit count `0.0`, avg retained emit count `0.0`, avg skinned count `25.0`, gpu skinned `25`, direct skinned frames `90/90`, drops events `0` commands `0` skinned `0`
- 10000: avg dirty emit count `0.0`, avg retained emit count `0.0`, avg skinned count `64.0`, gpu skinned `64`, direct skinned frames `90/90`, drops events `0` commands `0` skinned `0`
- 30000: avg dirty emit count `0.0`, avg retained emit count `0.0`, avg skinned count `214.2`, gpu skinned `214`, direct skinned frames `90/90`, drops events `0` commands `0` skinned `0`
