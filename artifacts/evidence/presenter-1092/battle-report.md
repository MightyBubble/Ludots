# Presenter BehaviorSlot SSOT

## Scenario Card
- Player goal: enter the Blacksmith presenter showcase and verify that output configuration is owned by each behavior slot.
- Map: `presenter_blacksmith_showcase`
- Launcher: `dotnet run --project src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj --no-restore -- launch presenter_blacksmith_showcase --adapter raylib`
- Build: Release launcher, branch `codex/issue-1092-deepseek`

## Timeline
- [T+000] `definition_load` -> presenter definitions load with output facts under `BehaviorSlot` payloads.
- [T+001] `slot_isolation_test` -> two WorldText slots retain independent text, style, motion, and slot indices (`0`, `11`).
- [T+002] `compiled_view_test` -> WorldText compiled `AssetBinding` is generated from `WorldTextConfig`; authoring a second binding is rejected.
- [T+003] `runtime_test` -> emit and fast-emit paths read slot motion/alpha and keep stable HUD ids per slot.
- [T+004] `raylib_launch` -> real `presenter_blacksmith_showcase` entry starts and loads the presenter catalog without host asset errors.
- [T+005] `adapter_screenshot` -> screenshot captures the Blacksmith scene, world HUD, and player control panel.

## Outcome
- success: yes
- presenter output authoring facts have one source: `BehaviorSlot` payloads.
- top-level mirrors are absent from `PresenterDefinition` and no runtime path reads them.
- the Raylib showcase launch is adapter-visible and produced `blacksmith.png`.

## Summary Stats
- targeted regression tests: 196 passed, 0 failed
- Core build: 0 errors
- screenshot: `blacksmith.png`
- launch log: `launch-2.log`
- adapter diagnostic: `raylib-diagnostic.log`
