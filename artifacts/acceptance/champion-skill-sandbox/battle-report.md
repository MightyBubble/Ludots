# Scenario: champion-skill-sandbox

## Header
- build: GasTests / ChampionSkillSandbox_PlayableFlow_WritesAcceptanceArtifacts
- map: champion_skill_sandbox
- clock: FixedFrame @ 60 Hz
- execution_timestamp_utc: 2026-04-07T05:26:38.0068598Z
- evidence_images: `screens/*.png`, `screens/timeline.png`
- evidence_note: PNGs are headless runtime evidence cards rendered from acceptance snapshots.

## Timeline
[T+001] champion_skill_sandbox loaded | default mode Action | default focus Duelist Alpha
[T+002] F1(Quick Cast) -> Select(Ezreal Cooldown) -> panel shows R blocked by cooldown state
[T+003] Select(Garen Courage) -> panel shows W active from toggle state
[T+004] Select(Jayce Hammer) -> panel routes to hammer-form Q/W/E/R
[T+005] Idle hover over enemy Target Dummy A shows a dedicated hover marker before any cast input
[T+006] Ezreal Alpha.Move(RMB) -> X 1180 to 1333 to create spacing with a visible path overlay
[T+007] Camera.Reset(F4) -> tactical view restored around the selected champion
[T+008] Ezreal Alpha.Cast(Mystic Shot) -> Target Dummy A | Hit | HP 220 -> 205
[T+009] Indicator hover over Target Dummy A shows an extra target marker before release
[T+010] Indicator mode hold-release previews Trueshot Barrage, then fires on release | HP 205 -> 181
[T+011] Jayce Cannon Q press-release path keeps cancel non-destructive, then left-click confirm lands on Target Dummy A | cancel HP 181 | preview reset=headless-late | confirm HP 164
[T+012] Select(Geomancer Alpha) -> panel exposes summon / zone / blocker / beam loadout
[T+013] Geomancer Alpha.Cast(Prismatic Beam) -> Target Dummy C | Hit | HP 196 -> 176
[T+014] Geomancer Alpha.Cast(Runic Beacon) -> summon spawned | hover-selectable | click-selected | owner-parent link copied
[T+015] Geomancer Alpha.Cast(Rune Field) -> zone manifestation spawned under Target Dummy C | periodic hit confirmed | HP 176 -> 170
[T+016] Geomancer Alpha.Cast(Stone Pillar) -> blocker manifestation spawned | selectable | bridged to nav/physics obstacle
[T+017] Select(Duelist Alpha) -> reposition to melee staging lane -> F5 enters Context Combo mode with Step In / Chain / Crowd Sweep / Opening Breaker / Space root
[T+017A] Duelist hover preview shows orange hover plus a separate resolved-target ring before Space commits
[T+018] Duelist Alpha.Space(ActionContext) -> headless preview stays quiet, but Step In still auto-locks Target Dummy F from the nearby target group | distance 342->22 | HP 196->181
[T+019] Duelist Alpha.Space(ActionContext) -> Chain Jab I auto-selected on engaged target | HP 181->170 | Q now routes to Chain Jab II
[T+020] Duelist Alpha.Space(ActionContext) -> Chain Jab II wins once Stage1 tag is live | Target Dummy F opened | HP 170->157
[T+021] Duelist Alpha.Space(ActionContext) -> Opening Breaker spends the opened window as the top-scored finisher | HP 157->129
[T+022] Duelist Alpha.Q smart-cast form routing proves Chain Jab I -> II -> Finish on Target Dummy F | HP 129->87
[T+023] Duelist Alpha.E(Crowd Sweep) cleaves the D/E/F cluster from melee follow-through | damaged_targets=2
[T+024] Select(Spell Engineer Alpha) -> panel exposes beacon / well / arena / guided laser showcase
[T+025] Spell Engineer Alpha.Cast(Spell Beacon) -> summon manifestation spawned with shared owner/team/map/parent contract
[T+026] Spell Engineer Alpha.Cast(Gravity Well) -> Target Dummy D zone tick confirmed | HP 206 -> 198
[T+027] Spell Engineer Alpha.Cast(Cataclysm Ring) -> 10 blocker segments spawned and sunk into box physics/nav obstacles
[T+028] Spell Engineer Alpha.Hold(Guided Laser) -> Dummy D hit, retarget to Dummy E rotates beam, Release(R) removes channel | HP D 198->185 | HP E 198->185

## Outcome
- result: success
- failure_branch: press-release aim cancel preserved target HP before confirm
- final_selected: Spell Engineer Alpha
- final_mode: ChampionSkillSandbox.Mode.SmartCast
- final_camera_target_cm: (2140, 1600)
- final_camera_distance_cm: 3900
- final_selection_ring_count: 5
- final_feedback_primitives: 54
- final_feedback_world_text: 35
- final_feedback_slash_ribbons: 0
- final_feedback_debug_draw: 0

## Summary Stats
- total_actions: 29
- selection_switches: 10
- hover_previews: 2
- move_commands: 1
- camera_resets: 1
- successful_hits: 11
- cancelled_casts: 1
- manifestation_spawns: 7
- manifestation_selections: 2
- median_tick_ms: 0.282
- max_tick_ms: 12.596
