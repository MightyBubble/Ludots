# Scenario: champion-skill-stress

## Header
- build: GasTests / ChampionSkillStress_PlayableFlow_WritesAcceptanceArtifacts
- map: champion_skill_stress
- clock: FixedFrame @ 60 Hz
- execution_timestamp_utc: 2026-08-17T05:39:44.3816969Z
- screenshots: `screens/*.svg`, `screens/timeline.svg`

## Timeline
[T+001] champion_skill_stress loaded | stress toolbar mounted for both team-size controls
[T+002] Formations saturated | A=48 (W/F/L/P 19/11/10/8) | B=48 (W/F/L/P 19/11/10/8)
[T+003] View=P1 Live | player selection container shows FireMageA, PriestA, WarriorA
[T+004] View=P1 Formation | formation container exposes 48 allied units through the same selection SSOT
[T+005] View=AI Targets | team-B commander publishes 48 focused enemy targets via selection containers
[T+006] View=Command Snapshot | command preview mirrors the current command-source entity after self-contained move order enqueue
[T+007] Frontline melee plus fireball/laser volleys engaged | peak_projectiles=21 | peak_primitives=198 | peak_world_text=396 | heal_observed=True
[T+008] Toolbar scale-up converged | A=56 | B=56 | injured A/B=22/20

## Outcome
- result: success
- failure_branch: toolbar scale-up must converge; otherwise stress spawn or order routing regressed
- final_toolbar: View Command Snapshot | A 48/56 | B 48/56 | Proj 2 peak 21 | HUD BTF
- final_view_primary: StressLaserMageA
- final_view_members: StressLaserMageA
- final_team_counts: A=56, B=56
- final_injured_counts: A=22, B=20
- final_projectiles: 4

## Summary Stats
- total_actions: 8
- formation_saturations: 1
- sustained_combat_windows: 1
- toolbar_scale_ups: 1
- selection_view_switches: 4
- command_snapshot_checks: 1
- peak_projectiles: 21
- peak_primitives: 198
- peak_world_text: 396
- heal_observed: True
- median_tick_ms: 3.299
- max_tick_ms: 20.372
