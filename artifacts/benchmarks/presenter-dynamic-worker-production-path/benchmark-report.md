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
| 3000 | 0.0000 ms | 123.6057 ms | 122.5747 ms | 83.6187 ms | 38.9511 ms | 0.2889 ms | 0.6577 ms | 26.8929 ms | 0.3149 ms | 0.0725 ms | 0.0000 ms | 0 | 0.0000 ms | 0 | 0.0005 ms | PresenterBehaviorSystem 26.8953 ms | RuntimeEntitySpawnSystem 68.1550 ms |
| 10000 | 0.0000 ms | 399.3775 ms | 398.6090 ms | 239.1641 ms | 159.4404 ms | 0.9072 ms | 1.5460 ms | 120.2931 ms | 0.8466 ms | 0.2644 ms | 0.0000 ms | 0 | 0.0000 ms | 0 | 0.0007 ms | PresenterBehaviorSystem 120.2958 ms | RuntimeEntitySpawnSystem 220.1489 ms |
| 30000 | 0.0000 ms | 3043.8382 ms | 3042.6936 ms | 633.5705 ms | 2409.1194 ms | 2.5761 ms | 4.5802 ms | 2027.7865 ms | 2.8228 ms | 0.9690 ms | 0.0000 ms | 0 | 0.0000 ms | 0 | 0.0005 ms | PresenterBehaviorSystem 2027.7903 ms | RuntimeEntitySpawnSystem 615.8885 ms |

## Stable Tick

| Count | Entities | Root Presenters | Attach Presenters | Skinned | Walking State | Animators | Grounded | Attached | Moved | Avg Tick | P95 Tick | Max Tick | Avg FPS | Avg Sim | Avg Pres | Avg Culling | Avg Transform Sync | Avg Behavior | Avg Animator | Avg Emit | Avg Dirty Emit | Avg Retained Emit | Avg Flush | top presentation | top simulation |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| 3000 | 3000 | 3000 | 3000 | 25 | 25 | 3000 | 3000 | 3000 | 3000 | 1.2364 ms | 1.8765 ms | 1.9568 ms | 808.8 | 0.1621 ms | 1.0666 ms | 0.0246 ms | 0.7125 ms | 0.0007 ms | 0.1361 ms | 0.0360 ms | 0.0000 ms | 0.0000 ms | 0.0000 ms | PresenterEntityTransformSyncSystem 0.7127 ms | DynamicWorkerCrowdMovementSystem 0.1415 ms |
| 10000 | 10000 | 10000 | 10000 | 64 | 64 | 10000 | 10000 | 10000 | 10000 | 4.4302 ms | 6.6731 ms | 7.3659 ms | 225.7 | 0.4938 ms | 3.9041 ms | 0.1013 ms | 2.6909 ms | 0.0041 ms | 0.4466 ms | 0.1393 ms | 0.0000 ms | 0.0000 ms | 0.0004 ms | PresenterEntityTransformSyncSystem 2.6915 ms | DynamicWorkerCrowdMovementSystem 0.4479 ms |
| 30000 | 30000 | 30000 | 30000 | 214 | 214 | 30000 | 30000 | 30000 | 30000 | 22.5357 ms | 28.9233 ms | 70.9471 ms | 44.4 | 2.2316 ms | 20.2396 ms | 0.5062 ms | 14.3181 ms | 0.0114 ms | 1.9310 ms | 0.8796 ms | 0.0000 ms | 0.0000 ms | 0.0006 ms | PresenterEntityTransformSyncSystem 14.3204 ms | DynamicWorkerCrowdMovementSystem 1.5638 ms |

- 3000: avg dirty emit count `0.0`, avg retained emit count `0.0`, avg skinned count `25.0`, gpu skinned `25`, direct skinned frames `90/90`, drops events `0` commands `0` skinned `0`
- 10000: avg dirty emit count `0.0`, avg retained emit count `0.0`, avg skinned count `64.0`, gpu skinned `64`, direct skinned frames `90/90`, drops events `0` commands `0` skinned `0`
- 30000: avg dirty emit count `0.0`, avg retained emit count `0.0`, avg skinned count `214.2`, gpu skinned `214`, direct skinned frames `90/90`, drops events `0` commands `0` skinned `0`
