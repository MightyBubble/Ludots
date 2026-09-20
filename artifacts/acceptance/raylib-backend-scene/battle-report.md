# Raylib Backend Scene Acceptance

## Run

- Branch: `codex/issue-1403`
- Base: `origin/main` at `e5ed15a976`
- Map: `instanced_batch_demo`
- Clock: `RealtimePacemaker`
- Captured: 2026-08-31 (Asia/Shanghai)

## Scenario

Given the `RaylibBackendSceneDemoMod` fixture is launched with the Raylib adapter and AgentBridge
And the startup map is `instanced_batch_demo`
When the map reaches its steady presented state
Then the backend-owned scene model is rendered without becoming a Core map entity
And the typed instanced batch still renders its 64-instance grid
And map-loaded work is released only after Core and backend presentation assets are resident

## Evidence

- Runtime: PID 55232, map `instanced_batch_demo`, AgentBridge port `47921`.
- Health: `pumpCount` increased from 49295 to 54608 across 700 ms.
- Session tick: 405-406; loaded mods include `RaylibBackendSceneDemoMod` and `AgentBridgeMod`.
- Entity query: `totalMatched=1`, only `Instanced Batch Demo Anchor` (entity 6).
- Presenter query: `totalMatched=1`, entity-anchored presenter (entity 7); no backend scene presenter/entity row.
- Integration test: `RaylibInstancedBatchDemoIntegrationTests.DemoGrid_ProducesOneCompletedResidentLane` passed; lane count `64`.
- Scene tests: `RaylibBackendSceneTests` passed, 8 tests.
- Manifest test: `MapPresentationAssetManifestTests` passed, 1 test; covers batch mesh, entity asset override, presenter child override, and instance-owned child subtree.
- Core completion-gate tests: `RenderAssetMapLoadCompletionGateTests` passed, 9 tests.
- Visual capture: `artifacts/agent-bridge/shots/issue-1403-backend-scene-final.png`.

## Outcome

- Result: passed.
- Required scene assets: 1; resident: 1; failed: 0.
- Core scene entities added by the backend layer: 0.
- Instanced batch lanes: 1; instances: 64; dropped: 0.
- Targeted assertions failed: 0.
- Residual test note: the broad `FullyQualifiedName~Raylib` run was stopped after a native-window test did not terminate; all issue-specific suites listed above completed independently.

## Load Ordering Observed

1. Core map config and entity anchor were created.
2. `MapLoaded` was deferred while the backend model was loading.
3. The glTF model and texture were uploaded.
4. `MapLoaded` fired after the combined completion gate became ready.
