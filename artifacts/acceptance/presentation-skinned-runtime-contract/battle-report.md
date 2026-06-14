# Scenario: presentation-skinned-runtime-contract

## Header
- scenario name: projection_map performer skinned vs static lane contract
- build/version: local PresentationTests
- seed/map/clock: deterministic fixture / camera_acceptance_projection / 5 ticks @ 60 Hz
- execution timestamp: 2026-04-26T19:06:25.7246778Z

## Timeline
- [T+005] Hero#2033545.Emit -> lane SkinnedMesh | Animator controller 1 bound | result = performer skinned contract valid
- [T+005] Dummy#2796549.Emit -> lane StaticMesh | Animator none | result = static performer lane stays separate
- [T+005] Dummy#2413110.Emit -> lane StaticMesh | Animator none | result = static performer lane stays separate

## Outcome
- success/failure decision: success
- failed assertions: none
- reason codes: skinned_lane_bound, static_lane_clean

## Summary Stats
- total actions: 3
- key damage/heal/control counters: not applicable
- dropped/budget/fuse counters: 0
