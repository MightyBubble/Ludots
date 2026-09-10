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
| 3000 | 0.0000 ms | 245.4551 ms | 203.9876 ms | 154.9413 ms | 49.0309 ms | 4.8201 ms | 0.7926 ms | 31.0294 ms | 1.1266 ms | 0.0740 ms | 0.0000 ms | 0 | 0.0000 ms | 0 | 0.0008 ms | PresenterBehaviorSystem 31.0306 ms | RuntimeEntitySpawnSystem 128.6285 ms |
| 10000 | 0.0000 ms | 743.4446 ms | 695.1525 ms | 452.5993 ms | 242.5379 ms | 12.6771 ms | 4.8460 ms | 168.5590 ms | 5.3007 ms | 0.2907 ms | 0.0000 ms | 0 | 0.0000 ms | 0 | 0.0006 ms | PresenterBehaviorSystem 168.5606 ms | RuntimeEntitySpawnSystem 412.3675 ms |
| 30000 | 0.0000 ms | 3558.2094 ms | 3514.9326 ms | 1087.5338 ms | 2427.3860 ms | 4.1060 ms | 8.9125 ms | 1867.4530 ms | 4.2182 ms | 1.4468 ms | 0.0000 ms | 0 | 0.0000 ms | 0 | 0.0006 ms | PresenterBehaviorSystem 1867.4550 ms | RuntimeEntitySpawnSystem 1047.0345 ms |

## Stable Tick

| Count | Entities | Root Presenters | Attach Presenters | Skinned | Walking State | Animators | Grounded | Attached | Moved | Avg Tick | P95 Tick | Max Tick | Avg FPS | Avg Sim | Avg Pres | Avg Culling | Avg Transform Sync | Avg Behavior | Avg Animator | Avg Emit | Avg Dirty Emit | Avg Retained Emit | Avg Flush | top presentation | top simulation |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| 3000 | 3000 | 3000 | 3000 | 25 | 25 | 3000 | 3000 | 3000 | 3000 | 2.9579 ms | 4.8361 ms | 5.2893 ms | 338.1 | 0.2938 ms | 2.6470 ms | 0.1268 ms | 1.2813 ms | 0.0017 ms | 0.7131 ms | 0.0676 ms | 0.0000 ms | 0.0000 ms | 0.0001 ms | PresenterEntityTransformSyncSystem 1.2892 ms | SpatialPartitionUpdateSystem 0.2569 ms |
| 10000 | 10000 | 10000 | 10000 | 64 | 64 | 10000 | 10000 | 10000 | 10000 | 8.7406 ms | 12.2205 ms | 13.5504 ms | 114.4 | 0.7710 ms | 7.9206 ms | 0.2248 ms | 5.4720 ms | 0.0057 ms | 0.9184 ms | 0.2691 ms | 0.0000 ms | 0.0000 ms | 0.0005 ms | PresenterEntityTransformSyncSystem 5.4729 ms | DynamicWorkerCrowdMovementSystem 0.8957 ms |
| 30000 | 30000 | 30000 | 30000 | 214 | 214 | 30000 | 30000 | 30000 | 30000 | 30.5967 ms | 39.8261 ms | 79.0712 ms | 32.7 | 2.6426 ms | 27.8925 ms | 0.6982 ms | 19.5964 ms | 0.0109 ms | 3.1193 ms | 1.0399 ms | 0.0000 ms | 0.0000 ms | 0.0006 ms | PresenterEntityTransformSyncSystem 19.5979 ms | DynamicWorkerCrowdMovementSystem 2.5187 ms |

- 3000: avg dirty emit count `0.0`, avg retained emit count `0.0`, avg skinned count `25.0`, gpu skinned `25`, direct skinned frames `90/90`, drops events `0` commands `0` skinned `0`
- 10000: avg dirty emit count `0.0`, avg retained emit count `0.0`, avg skinned count `64.0`, gpu skinned `64`, direct skinned frames `90/90`, drops events `0` commands `0` skinned `0`
- 30000: avg dirty emit count `0.0`, avg retained emit count `0.0`, avg skinned count `214.2`, gpu skinned `214`, direct skinned frames `90/90`, drops events `0` commands `0` skinned `0`
