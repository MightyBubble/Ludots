# Scenario: presentation-skinned-runtime-contract

## Header
- scenario name: projection_map presenter skinned vs static lane contract
- build/version: local PresentationTests
- seed/map/clock: deterministic fixture / camera_acceptance_projection / 5 ticks @ 60 Hz
- execution timestamp: 2026-09-11T08:20:17.7638047Z

## Timeline
- [T+005] Hero#3559614.Emit -> lane SkinnedMesh | Animator controller 1 bound | result = presenter skinned contract valid
- [T+005] Dummy#2796550.Emit -> lane StaticMesh | Animator none | result = static presenter lane stays separate
- [T+005] Dummy#3176145.Emit -> lane StaticMesh | Animator none | result = static presenter lane stays separate

## Outcome
- success/failure decision: success
- failed assertions: none
- reason codes: skinned_lane_bound, static_lane_clean

## Summary Stats
- total actions: 3
- key damage/heal/control counters: not applicable
- dropped/budget/fuse counters: 0
