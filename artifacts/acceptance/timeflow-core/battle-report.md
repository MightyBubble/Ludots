# Scenario Card: timeflow-core

## Intent
- Goal: prove that a shared Core time service composes scale and pause tokens without inventing a parallel scheduler.
- Gameplay domain: Core `TimeFlowService`, domain hierarchy, token composition, and `GasClockStepPolicy` step pacing.

## Determinism Inputs
- Seed: none; pure deterministic service/policy scenario
- Map: none
- Clock profile: `GasClockStepPolicy(stepEveryFixedTicks: 2)`
- Initial entities: none

## Action Script
1. Start with default `simulation=1000permille` and `gas=1000permille`.
2. Apply `simulation=500permille` and `gas=2000permille`; composed GAS effective scale remains `1000permille`.
3. Release the simulation scale token while keeping `gas=2000permille`.
4. Apply a simulation pause token and confirm all child domains stop.

## Expected Outcomes
- Primary success condition: child-domain effective scales follow parent composition and GAS step pacing tracks the effective GAS scale.
- Failure branch condition: a child domain continues advancing after the simulation parent is paused.
- Key metrics: fixed-frame total=`11`, gas-step total=`7`, final paused state=`true`.

## Evidence Artifacts
- `artifacts/acceptance/timeflow-core/trace.jsonl`
- `artifacts/acceptance/timeflow-core/battle-report.md`
- `artifacts/acceptance/timeflow-core/path.mmd`

## Timeline
- `baseline` -> sim=1000 gas=1000 fixed=4 step=2 paused=False
- `simulation_scaled` -> sim=500 gas=1000 fixed=8 step=4 paused=False
- `gas_scaled` -> sim=1000 gas=2000 fixed=11 step=7 paused=False
- `pause` -> sim=0 gas=0 fixed=11 step=7 paused=True

## Outcome
- success: yes
- verdict: Core TimeFlow now owns shared domain scaling, while GAS step pacing reuses the existing clock instead of forking a second runtime.
