# Issue: camera input drag-pan semantics still had a raw-screen-sign bug

Date: 2026-03-10
Reporter: Codex
Scope: `Core/Input` + `Core/Gameplay/Camera`
Severity: P1

## Trigger

- User-visible symptom: middle-mouse `drag pan` vertical motion was still reversed in the camera showcase flow.
- The earlier camera/input convergence work had already introduced mouse-delta plumbing, but the runtime path still needed a full semantic check from input sampling to camera behavior consumption.

## Reuse Baseline

- Registry/System reuse:
  - `PlayerInputHandler` - shared input runtime for mouse action sampling.
  - `CameraControllerFactory` - shared controller composition path.
  - `DragRotateBehavior` / `GrabDragPanBehavior` - shared logical camera behaviors.
- Pipeline reuse:
  - input config action binding pipeline via [`assets/Configs/Input/default_input.json`](assets/Configs/Input/default_input.json)
  - camera preset/controller wiring via [`src/Core/Gameplay/Camera/CameraControllerFactory.cs`](src/Core/Gameplay/Camera/CameraControllerFactory.cs)
- Test harness reuse:
  - `GasTests` targeted runtime tests

## Findings

### 1. Raw mouse position had already been replaced, but pan semantics were still wrong

- `GrabDragPanBehavior` now consumes pointer delta instead of absolute pointer position, which is the correct timing model for visual-frame mouse movement.
- However, the screen-delta to logical-target translation still used the wrong sign, so dragged ground content moved opposite to the cursor.
- Fix location:
  - [`src/Core/Gameplay/Camera/Behaviors/GrabDragPanBehavior.cs`](src/Core/Gameplay/Camera/Behaviors/GrabDragPanBehavior.cs)

### 2. Rotation and pan now use distinct input semantics

- Raw `<Mouse>/Delta` remains the physical pointer delta action.
- `Look` remains the semantic camera-look action with Y inversion handled in input processing.
- `GrabDragPanBehavior` consumes raw screen delta and performs the world-space mapping itself.
- Relevant files:
  - [`src/Core/Input/Runtime/PlayerInputHandler.cs`](src/Core/Input/Runtime/PlayerInputHandler.cs)
  - [`assets/Configs/Input/default_input.json`](assets/Configs/Input/default_input.json)
  - [`src/Core/Gameplay/Camera/CameraPreset.cs`](src/Core/Gameplay/Camera/CameraPreset.cs)
  - [`src/Core/Gameplay/Camera/CameraControllerFactory.cs`](src/Core/Gameplay/Camera/CameraControllerFactory.cs)

### 3. The missing acceptance gap was test coverage on camera drag semantics

- Added focused regression coverage for:
  - raw mouse delta + `Invert` processor semantics
  - drag rotate using positive-Y-up `Look`
  - grab drag pan following cursor movement on screen
- Relevant tests:
  - [`src/Tests/GasTests/PlayerInputHandlerHotPathTests.cs`](src/Tests/GasTests/PlayerInputHandlerHotPathTests.cs)
  - [`src/Tests/GasTests/CameraInputSemanticsTests.cs`](src/Tests/GasTests/CameraInputSemanticsTests.cs)

## Verification

- Passed:
  - `dotnet build src/Core/Ludots.Core.csproj -nologo`
  - `dotnet test src/Tests/GasTests/GasTests.csproj --filter "FullyQualifiedName~PlayerInputHandlerHotPathTests|FullyQualifiedName~CameraInputSemanticsTests" -nologo --no-restore /p:BuildProjectReferences=false`
- Note:
  - Full `GasTests` project-reference rebuild is currently blocked by unrelated UI Reactive namespace drift in benchmark/test mods, so this issue remains a repository-level validation blocker rather than a camera-runtime blocker.

## Remaining Non-goals For This Commit

- This commit does not claim full camera showcase acceptance inside the game window.
- This commit does not resolve the separate UI Reactive compile drift currently surfacing from:
  - `mods/GasBenchmarkMod`
  - `mods/ReactiveTestMod`
  - `mods/PerformanceMod`
