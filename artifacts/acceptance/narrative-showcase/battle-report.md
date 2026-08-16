# Scenario Card: narrative-showcase

## Header
- scenario: `narrative-showcase`
- build: `GameEngine 1.0.0.0`
- execution_timestamp_utc: `2026-08-16T09:23:37.5936441+00:00`
- map: `narrative_showcase_hub`
- clock: `fixed 1/60s`

## Intent
- Player goal: play a full quest/dialogue/cinematic loop that starts in a camera-led intro, branches on dialogue knowledge, wakes a shrine, defeats a spawned beast, and returns for an ending choice.
- Gameplay domain: shared Ludots ECS movement, interaction showcase combat/GAS, trigger callbacks, runtime entity spawning, virtual cameras, and a single reusable narrative frontend scene.

## Determinism Inputs
- Seed: none
- Map: `narrative_showcase_hub`
- Mods: `LudotsCoreMod`, `CoreInputMod`, `CameraProfilesMod`, `NarrativeFrontendMod`, `EntityInfoPanelsMod`, `InteractionShowcaseMod`, `NarrativeShowcaseMod`
- Clock profile: fixed `1/60s`, headless `GameEngine.Tick()` loop.
- Input source: real `InputConfigPipelineLoader` + `PlayerInputHandler` with deterministic backend injections.
- Narrative branches exercised: briefing lore path -> return mercy path.

## Action Script
1. Boot the real engine with the narrative showcase mod and load the hub map.
2. Advance the intro cinematic, then choose the lore branch and accept the trial in elder dialogue.
3. Move to the shrine through the production order path and trigger the reveal callback.
4. Damage the spawned beast through the inherited interaction combat input, then finish it through deterministic GAS effect application.
5. Return to the elder, choose the Mercy ending, and validate the trigger-driven GAS blessing reward.

## Expected Outcomes
- Primary success condition: quest, dialogue, cinematic, interaction, and reward callbacks stay on shared runtime infrastructure from start to finish.
- Failure branch condition: without prior lore knowledge, the Mercy branch remains unavailable at return dialogue.
- Key metrics: quest stage, trust/lore/ending variables, cinematic state, active UI surfaces, beast health, and reward movement speed delta.

## Evidence Artifacts
- `artifacts/acceptance/narrative-showcase/trace.jsonl`
- `artifacts/acceptance/narrative-showcase/battle-report.md`
- `artifacts/acceptance/narrative-showcase/path.mmd`
- `artifacts/acceptance/narrative-showcase/5w1h.md`
- `artifacts/acceptance/narrative-showcase/screens/001_map_loaded.png`
- `artifacts/acceptance/narrative-showcase/screens/002_intro_complete.png`
- `artifacts/acceptance/narrative-showcase/screens/003_briefing_branch_complete.png`
- `artifacts/acceptance/narrative-showcase/screens/004_shrine_interacted.png`
- `artifacts/acceptance/narrative-showcase/screens/005_beast_spawned.png`
- `artifacts/acceptance/narrative-showcase/screens/006_beast_pressured.png`
- `artifacts/acceptance/narrative-showcase/screens/007_beast_defeated.png`
- `artifacts/acceptance/narrative-showcase/screens/008_mercy_ending.png`
- `artifacts/acceptance/narrative-showcase/screens/timeline.png`

## Timeline
- [T+001] Loaded the narrative showcase hub; HUD mounted and quest entered briefing stage.
- [T+002] Advanced the intro cinematic through the shared narrative input path and handed off into elder dialogue.
- [T+003] Took the lore branch, raised shared narrative variables, and advanced the reusable quest runtime into the trial stage.
- [T+004] Drove the ECS move/order loop to the shrine and triggered the reveal cinematic through the showcase interaction system.
- [T+005] Completed the reveal cinematic, let the callback emit the spawn signal, and observed the beast arrive through the runtime entity queue.
- [T+006] Used Arcweaver's inherited combat input on the spawned beast; HP 220 -> 202.
- [T+007] Finished the encounter through GAS effects, which the narrative runtime converted into the return stage via signal tracking.
- [T+008] Returned to the elder, unlocked the Mercy branch from earlier lore knowledge, completed the quest, and received the trigger-driven GAS blessing reward.

## Outcome
- success: yes
- final quest: `Ashen Oath: Completed - Deliver Your Verdict`
- final variables: `trust=4 | lore=1 | ending=Mercy`
- final dialogue card: `Warden Mirelle: Then the valley keeps a memory instead of a scar. Ending: Mercy.`
- reason: the showcase stayed on `ConfigPipeline`, `NarrativeDirector`, `TriggerManager`, `RuntimeEntitySpawnQueue`, `EffectRequestQueue`, `PlayerInputHandler`, `EntityCollectionContextRuntime`, and the shared `NarrativeFrontendMod` scene owner.

## Summary Stats
- total_actions: `8`
- snapshots captured: `8`
- median headless tick: `0.164ms`
- max headless tick: `8.291ms`
- final_ui_excerpt: `Quest Tracker | Ashen Oath | Awaiting quest | Quest, stage, objective, and hint all come from NarrativeDirector state plus showcase config.`
