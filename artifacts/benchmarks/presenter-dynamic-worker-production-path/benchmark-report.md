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
| 3000 | 0.0000 ms | 1146.9389 ms | 1132.3054 ms | 959.1715 ms | 173.0567 ms | 12.9169 ms | 24.5050 ms | 72.8825 ms | 7.1321 ms | 2.6061 ms | 0.0000 ms | 0 | 0.0000 ms | 0 | 0.0017 ms | PresenterBehaviorSystem 72.8869 ms | RuntimeEntitySpawnSystem 666.5836 ms |
| 10000 | 0.0000 ms | 2221.4386 ms | 2210.2288 ms | 1828.6411 ms | 381.5438 ms | 12.8559 ms | 40.4675 ms | 165.2161 ms | 6.3489 ms | 1.3484 ms | 0.0000 ms | 0 | 0.0000 ms | 0 | 0.0018 ms | PresenterBehaviorSystem 165.2204 ms | RuntimeEntitySpawnSystem 1782.2389 ms |
| 30000 | 0.0000 ms | 5634.0063 ms | 5618.8379 ms | 4013.7854 ms | 1604.9926 ms | 30.4857 ms | 133.8974 ms | 851.4332 ms | 21.2525 ms | 5.9209 ms | 0.0000 ms | 0 | 0.0000 ms | 0 | 0.0023 ms | PresenterBehaviorSystem 851.4379 ms | RuntimeEntitySpawnSystem 3963.3423 ms |

## Stable Tick

| Count | Entities | Root Presenters | Attach Presenters | Skinned | Walking State | Animators | Grounded | Attached | Moved | Avg Tick | P95 Tick | Max Tick | Avg FPS | Avg Sim | Avg Pres | Avg Culling | Avg Transform Sync | Avg Behavior | Avg Animator | Avg Emit | Avg Dirty Emit | Avg Retained Emit | Avg Flush | top presentation | top simulation |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| 3000 | 3000 | 3000 | 3000 | 25 | 25 | 3000 | 3000 | 3000 | 3000 | 26.2824 ms | 37.9479 ms | 42.5228 ms | 38.0 | 1.0524 ms | 25.0449 ms | 0.3953 ms | 15.5011 ms | 4.4431 ms | 1.5877 ms | 0.6316 ms | 0.0000 ms | 0.0000 ms | 0.0028 ms | PresenterEntityTransformSyncSystem 15.5030 ms | MapEntityLifecycleObserverSystem 0.8451 ms |
| 10000 | 10000 | 10000 | 10000 | 64 | 64 | 10000 | 10000 | 10000 | 10000 | 65.1223 ms | 73.8394 ms | 81.2004 ms | 15.4 | 1.8943 ms | 63.0704 ms | 0.9343 ms | 38.6426 ms | 9.9299 ms | 5.0186 ms | 1.5933 ms | 0.0000 ms | 0.0000 ms | 0.0017 ms | PresenterEntityTransformSyncSystem 38.6449 ms | DynamicWorkerCrowdMovementSystem 2.1089 ms |
| 30000 | 30000 | 30000 | 30000 | 214 | 214 | 30000 | 30000 | 30000 | 30000 | 183.7827 ms | 206.4643 ms | 246.9830 ms | 5.4 | 4.8829 ms | 178.7406 ms | 2.5995 ms | 109.2460 ms | 28.3555 ms | 14.2426 ms | 4.8946 ms | 0.0000 ms | 0.0000 ms | 0.0020 ms | PresenterEntityTransformSyncSystem 109.2485 ms | DynamicWorkerCrowdMovementSystem 5.8839 ms |

- 3000: avg dirty emit count `0.0`, avg retained emit count `0.0`, avg skinned count `25.0`, gpu skinned `25`, direct skinned frames `90/90`, drops events `0` commands `0` skinned `0`
- 10000: avg dirty emit count `0.0`, avg retained emit count `0.0`, avg skinned count `64.0`, gpu skinned `64`, direct skinned frames `90/90`, drops events `0` commands `0` skinned `0`
- 30000: avg dirty emit count `0.0`, avg retained emit count `0.0`, avg skinned count `214.2`, gpu skinned `214`, direct skinned frames `90/90`, drops events `0` commands `0` skinned `0`
