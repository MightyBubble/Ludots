# Scenario: animator-runtime-mvp

## Header
- scenario name: trigger-driven presenter-blackboard animator runtime progression
- build/version: local PresentationTests
- seed/map/clock: deterministic unit fixture / in-memory world / 2 ticks
- controller id: 1
- execution timestamp: 2026-09-01T10:20:30.6543633Z

## Timeline
- [T+001] blackboard trigger param #12 consumed -> attack state entered immediately
- [T+002] attack clip reached end -> controller returned to idle

## Outcome
- success/failure decision: success
- failed assertions: none
- reason codes: trigger_consumed, state_progression_valid
