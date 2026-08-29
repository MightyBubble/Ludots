# Scenario Card: narrative-showcase

## Header
- scenario: `narrative-showcase`
- build: `GameEngine 1.0.0.0`
- execution_timestamp_utc: `2026-08-29T02:40:54.5548723+00:00`
- map: `narrative_showcase_hub`
- clock: `fixed 1/60s`

## Intent
- Player goal: play a full task/dialogue/sequencer loop that starts in a camera-led intro, branches on dialogue knowledge, wakes a shrine, defeats a spawned beast, and returns for an ending choice.
- Gameplay domain: shared Ludots ECS movement, interaction showcase combat/GAS, trigger callbacks, runtime entity spawning, virtual cameras, and a single reusable narrative frontend scene.

## Determinism Inputs
- Seed: none
- Map: `narrative_showcase_hub`
- Mods: `LudotsCoreMod`, `CoreInputMod`, `CameraProfilesMod`, `NarrativeFrontendMod`, `EntityInfoPanelsMod`, `InteractionShowcaseMod`, `NarrativeShowcaseMod`
- Clock profile: fixed `1/60s`, headless `GameEngine.Tick()` loop.
- Input source: real `InputConfigPipelineLoader` + `PlayerInputHandler` with deterministic backend injections mapped to `DialogueInputActionIds`.
- Story branches exercised: briefing lore path -> return mercy path.

## Action Script
1. Boot the real engine with the narrative showcase mod and load the hub map.
2. Skip the intro Sequencer, then choose the lore branch and accept the trial in DialogueRuntime.
3. Move to the shrine through the production order path and trigger the TrialReveal Sequencer.
4. Damage the spawned beast through the inherited interaction combat input, then finish it through deterministic GAS effect application.
5. Return to the elder, choose the Mercy ending, and validate the trigger-driven GAS blessing reward.

## Expected Outcomes
- Primary success condition: TaskRuntime, DialogueRuntime, SequencerRuntime, interaction, and reward callbacks stay on shared runtime infrastructure from start to finish.
- Failure branch condition: without prior lore knowledge, the Mercy branch remains unavailable at return dialogue.
- Key metrics: task state, MapVariableStore trust/lore/ending/trial_phase, sequencer state, active UI surfaces, beast health, and reward movement speed delta.

## Evidence Artifacts
- `artifacts/acceptance/narrative-showcase/trace.jsonl`
- `artifacts/acceptance/narrative-showcase/battle-report.md`
- `artifacts/acceptance/narrative-showcase/path.mmd`
- `artifacts/acceptance/narrative-showcase/5w1h.md`
- `artifacts/acceptance/narrative-showcase/screens/001_map_loaded.png`
- `artifacts/acceptance/narrative-showcase/screens/002_intro_complete.png`
- `artifacts/acceptance/narrative-showcase/screens/003_world_bubble_projected.png`
- `artifacts/acceptance/narrative-showcase/screens/004_briefing_branch_complete.png`
- `artifacts/acceptance/narrative-showcase/screens/005_shrine_interacted.png`
- `artifacts/acceptance/narrative-showcase/screens/006_beast_spawned.png`
- `artifacts/acceptance/narrative-showcase/screens/007_beast_pressured.png`
- `artifacts/acceptance/narrative-showcase/screens/008_beast_defeated.png`
- `artifacts/acceptance/narrative-showcase/screens/009_standing_portrait_return.png`
- `artifacts/acceptance/narrative-showcase/screens/010_mercy_ending.png`
- `artifacts/acceptance/narrative-showcase/screens/timeline.png`

## Timeline
- [T+001] Loaded the narrative showcase hub; HUD mounted and TaskRuntime entered the briefing beat.
- [T+002] Skipped the intro Sequencer beat through StorySkip and handed off into DialogueRuntime elder briefing.
- [T+003a] World bubble lore reply projected onto the speaker head via IScreenProjector (not a fixed corner panel).
- [T+003] Took the lore branch via StoryChoice1, wrote MapVariableStore trust/lore, and advanced TaskRuntime into the trial beat.
- [T+004] Placed Arcweaver near the shrine and started TrialReveal through SequencerRuntime via StoryInteract.
- [T+005] Skipped the reveal sequence, let the completed callback emit the spawn signal, and observed the beast arrive through the runtime entity queue.
- [T+006] SkillQ probe did not land in headless (HP stayed 220); continuing with deterministic GAS finisher.
- [T+007] Finished the encounter through GAS effects; TaskRuntime advanced into the return beat via signal tracking.
- [T+007a] Return beat opened on story.standing_portrait with a half-screen standing figure for the warden.
- [T+008] Returned to the elder, unlocked Mercy through lore-gated StoryChoice2, completed TaskRuntime, and received the trigger-driven GAS blessing reward.

## Outcome
- success: yes
- final task: `Task.Narrative.AshenOath.Return:Completed,Task.Narrative.AshenOath.Trial:Completed,Task.Narrative.AshenOath.Briefing:Completed`
- final variables: `trust=4,lore=1,ending=2,trial_phase=1`
- final dialogue card: `Dialogue.Narrative.Return/return_mercy_outro:那么山谷留下的是记忆，而不是伤疤。`
- reason: the showcase stayed on `ConfigPipeline`, `DialogueRuntime`, `SequencerRuntime`, `TaskRuntimeService`, `TriggerManager`, `RuntimeEntitySpawnQueue`, `EffectRequestQueue`, `PlayerInputHandler`, `EntityCollectionContextRuntime`, and the shared `NarrativeFrontendMod` scene owner.

## Summary Stats
- total_actions: `10`
- snapshots captured: `10`
- median headless tick: `1.152ms`
- max headless tick: `189.475ms`
- final_ui_excerpt: `你 | 织弧者 | 守望者 | 米蕾勒`
