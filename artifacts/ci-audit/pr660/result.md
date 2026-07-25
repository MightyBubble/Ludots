# CI Audit Gate - PR #660 / #689 Final Repair

- Date: 2026-07-25
- Worktree: `C:\001_AI\_codex_audit\Ludots-pr660-086d3f4-exact-20260724-1415`
- Branch: `codex/issues-649-651-ordering`
- Base checked: `origin/main` at `5712a4eef4cdb1011cc0694d52e77de95bfe4aaa`
- Remote PR head before this repair pass: `a829bdfe6df33b864908d397c6b1c2ee935fa6a9`

## Gate Summary

Local final audit: PASS.

This file records the current repair scope and local gate evidence. The exact pushed head is recorded in the PR body and #689 after push; this artifact is committed with the final repair changes and must not be read as evidence for older heads such as `086d3f4`, `b822b83`, or `a829bdfe`.

## Scope

This audit covers the #689 blockers for PR #660:

- batch `EntityIntake` admission-capacity handling is whole-batch transactional
- runtime capacity config fails fast when admission result/rejection capacity cannot cover worst case
- pending retry rejection publishes failed terminal outcome before clearing pending
- AbilityExec terminal interrupt/finish/fail paths reserve presentation capacity before state removal
- AbilityExec hot path has an architecture guard against direct `World.Add/Remove<AbilityExecInstance>`
- move-then-cast planning distinguishes not-applicable from typed rejection
- collection command-panel multi-member aiming rejects explicitly instead of opening one actor's aiming
- runtime spawn single and batch requests preflight relationship, owner/team, receipt, presentation, and effect capacity before dequeue
- required ArchitectureTests cleanup no longer flakes on Windows descendant process directory locks

Visual review packets are scoped out for this headless runtime repair. No gameplay visuals, showcase layout, or player-facing asset changed in this pass.

## Packet And Artifact Check

- Required current-scope GAS self-review artifact: `artifacts/gas-composition-gate.md` - present and updated for #689.
- Required current-scope CI audit artifacts: `artifacts/ci-audit/pr660/result.md` and `artifacts/ci-audit/pr660/result.json` - present and updated.
- Current-scope blocked packets: none found under `artifacts`.
- Visual review / handoff packets: not required for this current headless repair scope.

## Evidence

- `dotnet test src\Tests\ArchitectureTests\ArchitectureTests.csproj -c Debug --no-restore --nologo`: PASS, 188/188.
- `dotnet test src\Tests\GasTests\GasTests.csproj -c Debug --no-restore --filter "FullyQualifiedName~InputOrderAbilityAuditTests|FullyQualifiedName~InputOrderContractTests|FullyQualifiedName~CollectionGasEntityCommandPanelAggregationTests|FullyQualifiedName~RoadNetworkShowcaseTests|FullyQualifiedName~OrderCompositePlannerTests" --nologo`: PASS, 198/198.
- `dotnet test src\Tests\GasTests\GasTests.csproj -c Debug --no-restore --filter "FullyQualifiedName~GasExecutionBudgetTests" --nologo --logger "console;verbosity=minimal"`: PASS, 34/34.
- `dotnet test src\Tests\GasTests\GasTests.csproj -c Debug --no-restore --filter "FullyQualifiedName~CollectionGasEntityCommandPanelAggregationTests" --nologo --logger "console;verbosity=minimal"`: PASS, 7/7.
- `dotnet test src\Tests\GasTests\GasTests.csproj -c Debug --no-restore --filter "FullyQualifiedName~RuntimeEntitySpawnSystem_SingleUnitTypeMissingTeamRepresentative" --nologo --logger "console;verbosity=minimal"`: PASS, 1/1.
- `dotnet test src\Tests\GasTests\GasTests.csproj -c Debug --no-restore --filter "FullyQualifiedName~OrderCompositePlannerTests|FullyQualifiedName~OrderBufferSystem_BatchEntityAdmissionCapacityMiss" --nologo --logger "console;verbosity=minimal"`: PASS, 9/9.
- `dotnet test src\Tests\ArchitectureTests\ArchitectureTests.csproj -c Debug --no-restore --filter "FullyQualifiedName~GasAbilityExecHotPath_DoesNotCallWorldAddOrRemoveDirectly" --nologo --logger "console;verbosity=minimal"`: PASS, 1/1.
- `git diff --check origin/main...HEAD`: PASS.

## Notes

Three unrelated artifact files were dirty before this repair and are intentionally excluded from this evidence commit:

- `artifacts/benchmarks/entity-query-tactics-showcase/benchmark-report.md`
- `artifacts/showcases/capability-standard-physics2d-stress/acceptance.md`
- `artifacts/showcases/capability-standard-physics2d-stress/keyframes.jsonl`

ci.audit.completed
