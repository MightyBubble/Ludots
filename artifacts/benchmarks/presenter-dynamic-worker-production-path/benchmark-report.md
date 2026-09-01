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
| 3000 | 0.0000 ms | 175.9054 ms | 174.0251 ms | 130.8157 ms | 43.1997 ms | 1.3547 ms | 5.7216 ms | 18.3818 ms | 0.7390 ms | 0.1523 ms | 0.0000 ms | 0 | 0.0000 ms | 0 | 0.0016 ms | PresenterBehaviorSystem 18.3835 ms | RuntimeEntitySpawnSystem 129.1572 ms |
| 10000 | 0.0000 ms | 554.9371 ms | 554.2377 ms | 398.7626 ms | 155.4640 ms | 4.1914 ms | 19.2314 ms | 72.5282 ms | 2.4220 ms | 0.5969 ms | 0.0000 ms | 0 | 0.0000 ms | 0 | 0.0018 ms | PresenterBehaviorSystem 72.5307 ms | RuntimeEntitySpawnSystem 397.2940 ms |
| 30000 | 0.0000 ms | 2291.3282 ms | 2289.6379 ms | 1232.7565 ms | 1056.8712 ms | 13.0180 ms | 59.6894 ms | 529.2743 ms | 7.6837 ms | 2.1857 ms | 0.0000 ms | 0 | 0.0000 ms | 0 | 0.0016 ms | PresenterBehaviorSystem 529.2776 ms | RuntimeEntitySpawnSystem 1229.0800 ms |

## Stable Tick

| Count | Entities | Root Presenters | Attach Presenters | Skinned | Walking State | Animators | Grounded | Attached | Moved | Avg Tick | P95 Tick | Max Tick | Avg FPS | Avg Sim | Avg Pres | Avg Culling | Avg Transform Sync | Avg Behavior | Avg Animator | Avg Emit | Avg Dirty Emit | Avg Retained Emit | Avg Flush | top presentation | top simulation |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| 3000 | 3000 | 3000 | 3000 | 25 | 25 | 3000 | 3000 | 3000 | 3000 | 9.3552 ms | 10.9010 ms | 28.8197 ms | 106.9 | 0.5208 ms | 8.7621 ms | 0.1871 ms | 5.5620 ms | 1.3943 ms | 0.5531 ms | 0.1600 ms | 0.0000 ms | 0.0000 ms | 0.0015 ms | PresenterEntityTransformSyncSystem 5.5628 ms | DynamicWorkerCrowdMovementSystem 0.2462 ms |
| 10000 | 10000 | 10000 | 10000 | 64 | 64 | 10000 | 10000 | 10000 | 10000 | 31.4058 ms | 36.0387 ms | 55.2329 ms | 31.8 | 1.1461 ms | 30.1723 ms | 0.5220 ms | 19.4502 ms | 4.8755 ms | 1.9040 ms | 0.5895 ms | 0.0000 ms | 0.0000 ms | 0.0016 ms | PresenterEntityTransformSyncSystem 19.4519 ms | DynamicWorkerCrowdMovementSystem 0.8191 ms |
| 30000 | 30000 | 30000 | 30000 | 214 | 214 | 30000 | 30000 | 30000 | 30000 | 98.7101 ms | 110.7392 ms | 242.7674 ms | 10.1 | 4.4810 ms | 94.1313 ms | 1.5229 ms | 60.3445 ms | 15.6326 ms | 5.9889 ms | 2.1406 ms | 0.0000 ms | 0.0000 ms | 0.0017 ms | PresenterEntityTransformSyncSystem 60.3462 ms | DynamicWorkerCrowdMovementSystem 2.4914 ms |

- 3000: avg dirty emit count `0.0`, avg retained emit count `0.0`, avg skinned count `25.0`, gpu skinned `25`, direct skinned frames `90/90`, drops events `0` commands `0` skinned `0`
- 10000: avg dirty emit count `0.0`, avg retained emit count `0.0`, avg skinned count `64.0`, gpu skinned `64`, direct skinned frames `90/90`, drops events `0` commands `0` skinned `0`
- 30000: avg dirty emit count `0.0`, avg retained emit count `0.0`, avg skinned count `214.2`, gpu skinned `214`, direct skinned frames `90/90`, drops events `0` commands `0` skinned `0`
