# Scenario: champion-skill-sandbox

## Header
- build: GasTests / ChampionSkillSandbox_PlayableFlow_WritesAcceptanceArtifacts
- map: champion_skill_sandbox
- clock: FixedFrame @ 60 Hz
- execution_timestamp_utc: 2026-08-16T17:39:06.3271110Z

## Timeline
[T+001] champion_skill_sandbox loaded | default mode Quick Cast | default focus Ezreal Alpha
[T+002] Select(Ezreal Cooldown) -> panel shows R blocked by cooldown state
[T+003] Select(Garen Courage) -> panel shows W active from toggle state
[T+004] Select(Jayce Hammer) -> panel routes to hammer-form Q/W/E/R
[T+005] Idle hover over Target Dummy A shows a dedicated hover marker before any cast input
[T+006] Ezreal Alpha.Move(RMB) -> X 1180 to 1264 to create spacing with a visible path overlay
[T+007] Camera.Reset(F4) -> tactical view restored to sandbox default pose
[T+008] Ezreal Alpha.Cast(Mystic Shot) -> Target Dummy A | Hit | HP 220 -> 205
[T+009] Indicator hover over Target Dummy A shows an extra target marker before release
[T+010] Indicator mode hold-release previews Trueshot Barrage, then fires on release | HP 205 -> 181
[T+011] Press-release aim cast shows confirm cursor for Jayce Cannon Q | cancel keeps HP 181 | confirm hits to 164
[T+011] Select(Geomancer Alpha) -> panel exposes summon / zone / blocker / beam loadout
[T+012] Geomancer Alpha.Cast(Prismatic Beam) -> Target Dummy C | Hit | HP 220 -> 200
[T+013] Geomancer Alpha.Cast(Runic Beacon) -> summon spawned | hover-selectable | owner-parent link copied
[T+014] Geomancer Alpha.Cast(Rune Field) -> zone manifestation spawned under Target Dummy C | periodic hit confirmed | HP 200 -> 194
[T+015] Geomancer Alpha.Cast(Stone Pillar) -> blocker manifestation spawned | selectable | bridged to nav/physics obstacle
[T+016] Select(Spell Engineer Alpha) -> panel exposes beacon / well / arena / guided laser showcase
[T+017] Spell Engineer Alpha.Cast(Spell Beacon) -> summon manifestation spawned with shared owner/team/map/parent contract
[T+018] Spell Engineer Alpha.Cast(Gravity Well) -> Target Dummy D zone tick confirmed | HP 220 -> 212
[T+019] Spell Engineer Alpha.Cast(Cataclysm Ring) -> 10 blocker segments spawned and sunk into box physics/nav obstacles
[T+020] Spell Engineer Alpha.Hold(Guided Laser) -> Dummy D hit, retarget to Dummy E rotates beam, Release(R) removes channel | HP D 212->199 | HP E 212->199

## Outcome
- result: success
- failure_branch: press-release aim cancel preserved target HP before confirm
- final_selected: Spell Engineer Alpha
- final_mode: ChampionSkillSandbox.Mode.SmartCast
- final_camera_target_cm: (1850, 580)
- final_camera_distance_cm: 6500
- final_selection_ring_count: 21
- final_feedback_primitives: 22
- final_feedback_world_text: 22

## Summary Stats
- total_actions: 15
- selection_switches: 9
- hover_previews: 2
- move_commands: 1
- camera_resets: 1
- successful_hits: 5
- cancelled_casts: 1
- manifestation_spawns: 3
- manifestation_selections: 2
- median_tick_ms: 0.801
- max_tick_ms: 13.045
