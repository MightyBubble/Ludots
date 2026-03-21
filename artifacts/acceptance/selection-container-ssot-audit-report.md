# Selection Container SSOT Acceptance Report

Date: 2026-03-20
Build target: `src/Tests/GasTests/GasTests.csproj`
Primary acceptance tests:

- `InteractionSelectionConvergenceTests`
- `ChampionSkillSandboxConfigTests`
- `ChampionSkillSandboxPlayableAcceptanceTests`

## 1. Scenario Design

### Scenario A: Core selection convergence

Goal:

- prove that formal selection truth converges on containers/member relations
- prove that queued orders can retain selection snapshots without a fixed-size entity payload

Coverage:

- replace/add/remove selection membership
- view resolution through current viewer + view key
- order snapshot stability
- lease cleanup after container is no longer referenced

### Scenario B: Champion sandbox playable flow

Goal:

- prove that normal player-facing selection, panel routing, cast modes, hover, camera reset, and order input all read the same formal selection/view contract

Coverage:

- select different champions
- confirm panel detail labels change with form/state
- move order from current selection
- F4 camera reset
- smart cast, indicator cast, press-release aim cast

### Scenario C: Champion stress multi-view flow

Goal:

- prove that the same runtime can inspect multiple selection owners/views without a compatibility bridge

Coverage:

- player live selection
- player formation selection
- AI target selection
- AI formation selection
- command snapshot view
- scale-up under combat load

## 2. Acceptance Evidence

Sandbox playable evidence:

- `artifacts/acceptance/champion-skill-sandbox/battle-report.md`
- `artifacts/acceptance/champion-skill-sandbox/trace.jsonl`
- `artifacts/acceptance/champion-skill-sandbox/path.mmd`

Stress multi-view evidence:

- `artifacts/acceptance/champion-skill-stress/battle-report.md`
- `artifacts/acceptance/champion-skill-stress/trace.jsonl`
- `artifacts/acceptance/champion-skill-stress/path.mmd`
- `artifacts/acceptance/champion-skill-stress/screens/003_player_live_view.svg`
- `artifacts/acceptance/champion-skill-stress/screens/004_player_formation_view.svg`
- `artifacts/acceptance/champion-skill-stress/screens/005_ai_target_view.svg`
- `artifacts/acceptance/champion-skill-stress/screens/006_command_snapshot_view.svg`
- `artifacts/acceptance/champion-skill-stress/screens/timeline.svg`

Documentation and audit evidence:

- `docs/architecture/entity_selection_architecture.md`
- `docs/rfcs/RFC-0059-entity-selection-container-ssot.md`
- `artifacts/doc-governance-report.md`
- `artifacts/techdebt/2026-03-20-selection-container-ssot-redesign.md`

## 3. Observed Outcomes

Delivered outcomes:

- no hardcoded `64` cap remains in the delivered selection/order path
- selection truth is stored as container entities plus member relation entities
- orders hold `OrderSelectionReference` instead of a fixed-capacity selection payload
- queued orders can keep snapshot containers alive through selection leases
- champion stress can switch view across player live, player formation, AI targets, AI formation, and command snapshot
- command snapshot remains stable after live selection mutates
- panel targeting resolves from current viewed primary rather than a compatibility selected-entity mirror

## 4. Test Execution

Executed command:

```powershell
dotnet test src/Tests/GasTests/GasTests.csproj -c Debug /nologo --no-build --filter "FullyQualifiedName~InteractionSelectionConvergenceTests|FullyQualifiedName~ChampionSkillSandboxConfigTests|FullyQualifiedName~ChampionSkillSandboxPlayableAcceptanceTests"
```

Result:

- Passed: 29
- Failed: 0
- Skipped: 0

## 5. Residual Debt

Remaining migration debt is explicitly tracked in:

- `artifacts/techdebt/2026-03-20-selection-container-ssot-redesign.md`

This debt is outside the accepted SSOT path and must not be treated as valid architecture.
