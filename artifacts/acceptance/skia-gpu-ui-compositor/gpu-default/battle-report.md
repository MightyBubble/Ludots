# Scenario Card: rfc0065_entity_command_panel_showcase

## Intent
- Player goal: verify the entity command panel showcase is launchable and has standard recorder evidence for trigger-owned command panel review.
- Gameplay domain: RFC0065 showcase mod `EntityCommandPanelShowcaseMod` recorded through launcher evidence artifact support.
- Recorder scope: launcher-side deterministic artifact bundle; this does not claim real Raylib or CEF framebuffer recording.

## Determinism Inputs
- Seed: none
- Map/config hint: `mods/showcases/entity_command_panel/EntityCommandPanelShowcaseMod/assets`
- Adapter: `raylib`
- Launch command: `.\scripts\run-mod-launcher.cmd cli launch preset:entity_command_panel_raylib --adapter raylib --record artifacts/acceptance/skia-gpu-ui-compositor/gpu-default`
- Root mods: `EntityCommandPanelShowcaseMod`
- Ordered mods: `LudotsCoreMod, EntityCommandPanelMod, CoreInputMod, CameraProfilesMod, EntityInfoPanelsMod, InteractionShowcaseMod, EntityCommandPanelShowcaseMod`
- Evidence images: `screens/000_launch_plan.png`, `screens/001_showcase_contract.png`, `screens/002_evidence_bundle.png`, `screens/timeline.png`

## Action Script
1. Resolve the launcher plan and root mod list.
2. Match the root mod against the RFC0065 showcase recorder profiles.
3. Emit the standard launcher evidence files for review and automation.
4. Emit simple PNG frames and a timeline sheet that visualize recorder artifact stages.

## Expected Outcomes
- battle-report.md describes the launch-plan artifact recording and command-panel showcase scope.
- trace.jsonl records launch-plan, showcase-contract, and evidence-bundle events.
- path.mmd shows the recorder dispatch path from launch plan to standard evidence files.
- summary.json exposes scenario, adapter, root mods, ordered mods, and artifact mode.
- visible-checklist.md lists the recorder PNG frames reviewers should inspect.

## Timeline
- [T+000] EntityCommandPanelShowcaseMod.000_launch_plan -> status=supported | adapter=raylib | mode=launcher-recorder-artifacts
- [T+001] EntityCommandPanelShowcaseMod.001_showcase_contract -> status=documented | adapter=raylib | mode=launcher-recorder-artifacts
- [T+002] EntityCommandPanelShowcaseMod.002_evidence_bundle -> status=complete | adapter=raylib | mode=launcher-recorder-artifacts

## Outcome
- success: yes
- verdict: RFC0065 showcase has a registered launcher evidence recorder artifact profile.
- reason: the unsupported-scenario branch was avoided and standard evidence files were written.

## Summary Stats
- screenshot captures: `3`
- root mod count: `1`
- ordered mod count: `7`
- normalized signature: `rfc0065_entity_command_panel_showcase|adapter=raylib|roots=EntityCommandPanelShowcaseMod|selectors=preset:entity_command_panel_raylib|mode=launcher-recorder-artifacts`
- reusable wiring: `LauncherLaunchPlan`, evidence recorder scenario dispatch, Skia PNG artifact writer
