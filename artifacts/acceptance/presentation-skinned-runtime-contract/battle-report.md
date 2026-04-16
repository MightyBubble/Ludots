# Scenario: presentation-skinned-runtime-contract

## Header
- scenario name: projection_map skinned vs static lane contract
- build/version: local PresentationTests
- seed/map/clock: deterministic fixture / camera_acceptance_projection / 5 ticks @ 60 Hz
- execution timestamp: 2026-04-16T04:29:30.9242949Z

## Timeline
- [T+005] Hero#1.Spawn -> lane SkinnedMesh | Animator controller 1 bound | result = skinned runtime contract valid
- [T+005] Dummy#3.Spawn -> lane StaticMesh | Animator none | result = static dirty-sync lane stays separate
- [T+005] Dummy#2.Spawn -> lane StaticMesh | Animator none | result = static dirty-sync lane stays separate

## Outcome
- success/failure decision: success
- failed assertions: none
- reason codes: skinned_lane_bound, static_lane_clean

## Summary Stats
- total actions: 3
- key damage/heal/control counters: not applicable
- dropped/budget/fuse counters: 0
