# Scenario Card: champion-control-showcase

## Intent
- Player goal: inspect and play a reusable control-buff showcase with visible slow, silence, root, and stun behavior.
- Gameplay domain: real `ChampionSkillSandboxMod` map runtime plus reusable `CommonControlBuffsMod` effect/tag/sink infrastructure.

## Determinism Inputs
- Seed: none
- Map: `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/Maps/champion_control_showcase.json`
- Clock profile: fixed `1/60s`
- Initial entities: `Control Marshal`, `Control Runner`, `Control Caster`
- Evidence images: `artifacts/acceptance/champion-control-showcase/screens/*.svg`, `artifacts/acceptance/champion-control-showcase/screens/timeline.svg`

## Action Script
1. Load the playable control showcase map and verify the overlay and marshal loadout.
2. Drive the runner through the real move-order path, then fire the marshal's Q/E/R control skills through cast orders.
3. Submit a real hostile cast from the caster, then fire the marshal's W/R control skills to prove startup rejection and active-cast interrupt.
4. Write trace, path, battle report, and screenshot frames for human review.

## Timeline
- [T+001] Control showcase loaded | marshal selected | overlay exposes Q slow / W silence / E root / R stun
- [T+002] Marshal Q -> Runner | Slow | MoveSpeed 360->220 | travel 97cm -> 33cm
- [T+003] Marshal E -> Runner | Root | MoveBlocked active | control sink drives nav max speed to 0
- [T+004] Marshal R -> Runner | Stun | ActionBlocked=1 | movement and action both gated
- [T+005] Caster -> Marshal | Arc Pulse hit | HP 170->160
- [T+006] Marshal W -> Caster | Silence | cast startup rejected before exec starts
- [T+007] Marshal R -> Caster | Stun mid-cast | active exec interrupted before Arc Pulse damage resolves

## Outcome
- result: success
- runner_baseline_travel_cm: 97
- runner_slowed_travel_cm: 33
- runner_recovery_travel_cm: 92
- final_selected_entity: Control Caster
- final_runner_tags: (none)
- final_caster_tags: Stunned, CooldownQ
- final_caster_exec: Idle

## Summary Stats
- total_actions: 7
- screenshot_captures: 7
- reusable_effects_proven: slow, silence, root, stun
- sink_projection_proven: move-block and action-block
- cast_gate_reuse_proven: silence startup rejection, stun interrupt
- median_tick_ms: 0.592
- max_tick_ms: 85.38
