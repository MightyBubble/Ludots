# Tech Debt Report: TD-2026-03-17-presenter-instance-buffer-capacity

Date: 2026-03-17
Reporter: Codex
Owner: Core Presentation / Engine
Severity: P1
Scope: Cross-layer

## Trigger
- Scenario: `champion_skill_stress`
- Entry point: `ChampionSkillSandbox_StressMap_LoadsToolbarControlsAndMaintainsCombatFormations`
- Repro steps:
  1. Load `champion_skill_stress`.
  2. Let both teams saturate to the default `48 vs 48`.
  3. Sustain combat long enough for fireball and laser projectile presenters to accumulate.
  4. Observe `PresenterRuntimeSystem.HandleCreatePresenter` throw `PresenterInstanceBuffer is full while creating a presenter instance.`

## Evidence
- `src/Core/Engine/GameEngine.cs`
- `src/Core/Presentation/Systems/PresenterRuntimeSystem.cs`
- `src/Tests/GasTests/Production/ChampionSkillSandboxConfigTests.cs`
- `src/Tests/GasTests/Production/ChampionSkillSandboxPlayableAcceptanceTests.cs`
- `artifacts/acceptance/champion-skill-stress/trace.jsonl`

## Impact
- User-visible impact: the stress battlefield crashes before sustained projectile combat can be validated.
- Correctness/stability risk: presentation runtime hard-stops even though gameplay/GAS continues to produce legitimate projectile cues.
- Blast radius: any high-throughput mod that relies on persistent presenter instances can fail once active projectile/cue concurrency exceeds the engine default.

## Fuse Decision
- Mode: explicit-degrade
- Reason: the failing path was caused by a shared Core buffer budget that was too small for a supported showcase workload. Immediate containment is to raise the shared capacity ceiling so the feature can run, while explicitly tracking that presentation budgets are still static and not scenario-configurable.
- Observability fields:
  - debt_id: `TD-2026-03-17-presenter-instance-buffer-capacity`
  - fuse_mode: `explicit-degrade`
  - scenario_id: `champion_skill_stress`
  - reason_code: `core.presentation.presenter_instance_capacity_exhausted`

## Containment and Follow-up
- Immediate containment: raise `PresenterInstanceBufferCapacity` in `GameEngine` from the implicit default (`256`) to an explicit Core constant (`4096`) and keep stress acceptance coverage in CI.
- Permanent fix direction: move presentation buffer budgets to explicit engine/config knobs with runtime telemetry so stress maps can scale safely without relying on hard-coded global constants.
- Target milestone: next presentation budget pass for stress/perf harness work.
