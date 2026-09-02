# Scenario: animation-layered-prototypes

## Header
- scenario name: raylib layered tank + humanoid prototype acceptance
- build/version: local PresentationTests
- seed/map/clock: deterministic fixture / animation_acceptance_entry / 12 ticks @ 60 Hz
- execution timestamp: 2026-09-01T10:20:30.5804589Z

## Timeline
- [T+012] Tank prototype resolves profile -> state clip -> raylib/ue5 locators for the vehicle surrogate.
- [T+012] Humanoid prototype resolves the same chain for layered locomotion + aim + recoil.
- [T+012] Static baseline entity remains on static lane with profile id 0.

## Outcome
- success/failure decision: success
- failed assertions: none
- reason codes: layered_tank_visible, layered_humanoid_visible, static_lane_separate, profile_locator_chain_valid

## Summary Stats
- total actions: 3
- layered tank visuals: 1
- layered humanoid visuals: 1
- static baseline visuals: 1
