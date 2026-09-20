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
| 3000 | 0.0000 ms | 409.9838 ms | 408.3000 ms | 342.4823 ms | 65.7987 ms | 2.6871 ms | 8.9311 ms | 34.6480 ms | 1.3985 ms | 0.2686 ms | 0.0000 ms | 0 | 0.0000 ms | 0 | 0.0019 ms | PresenterBehaviorSystem 34.6498 ms | RuntimeEntitySpawnSystem 303.3378 ms |
| 10000 | 0.0000 ms | 1159.7518 ms | 1158.8041 ms | 918.7544 ms | 240.0311 ms | 7.6455 ms | 29.6331 ms | 119.8446 ms | 4.6182 ms | 0.8962 ms | 0.0000 ms | 0 | 0.0000 ms | 0 | 0.0023 ms | PresenterBehaviorSystem 119.8469 ms | RuntimeEntitySpawnSystem 881.7898 ms |
| 30000 | 0.0000 ms | 4379.9580 ms | 4378.2656 ms | 2874.0171 ms | 1504.2302 ms | 19.6748 ms | 90.7767 ms | 688.3127 ms | 18.2739 ms | 3.0754 ms | 0.0000 ms | 0 | 0.0000 ms | 0 | 0.0024 ms | PresenterBehaviorSystem 688.3159 ms | RuntimeEntitySpawnSystem 2836.4324 ms |

## Stable Tick

| Count | Entities | Root Presenters | Attach Presenters | Skinned | Walking State | Animators | Grounded | Attached | Moved | Avg Tick | P95 Tick | Max Tick | Avg FPS | Avg Sim | Avg Pres | Avg Culling | Avg Transform Sync | Avg Behavior | Avg Animator | Avg Emit | Avg Dirty Emit | Avg Retained Emit | Avg Flush | top presentation | top simulation |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| 3000 | 3000 | 3000 | 3000 | 25 | 25 | 3000 | 3000 | 3000 | 3000 | 15.4131 ms | 19.7726 ms | 21.8546 ms | 64.9 | 0.5791 ms | 14.7639 ms | 0.3167 ms | 9.2266 ms | 2.1862 ms | 1.0930 ms | 0.2988 ms | 0.0000 ms | 0.0000 ms | 0.0009 ms | PresenterEntityTransformSyncSystem 9.2273 ms | DynamicWorkerCrowdMovementSystem 0.4908 ms |
| 10000 | 10000 | 10000 | 10000 | 64 | 64 | 10000 | 10000 | 10000 | 10000 | 55.0453 ms | 67.5540 ms | 71.9667 ms | 18.2 | 1.6586 ms | 53.2804 ms | 0.8865 ms | 33.8138 ms | 7.9471 ms | 3.9601 ms | 1.0861 ms | 0.0000 ms | 0.0000 ms | 0.0017 ms | PresenterEntityTransformSyncSystem 33.8150 ms | DynamicWorkerCrowdMovementSystem 1.6530 ms |
| 30000 | 30000 | 30000 | 30000 | 214 | 214 | 30000 | 30000 | 30000 | 30000 | 153.0877 ms | 186.8639 ms | 257.1815 ms | 6.5 | 5.3419 ms | 147.6251 ms | 2.2563 ms | 93.5333 ms | 22.3638 ms | 11.2296 ms | 3.0912 ms | 0.0000 ms | 0.0000 ms | 0.0020 ms | PresenterEntityTransformSyncSystem 93.5348 ms | DynamicWorkerCrowdMovementSystem 4.6663 ms |

- 3000: avg dirty emit count `0.0`, avg retained emit count `0.0`, avg skinned count `25.0`, gpu skinned `25`, direct skinned frames `90/90`, drops events `0` commands `0` skinned `0`
- 10000: avg dirty emit count `0.0`, avg retained emit count `0.0`, avg skinned count `64.0`, gpu skinned `64`, direct skinned frames `90/90`, drops events `0` commands `0` skinned `0`
- 30000: avg dirty emit count `0.0`, avg retained emit count `0.0`, avg skinned count `214.2`, gpu skinned `214`, direct skinned frames `90/90`, drops events `0` commands `0` skinned `0`
