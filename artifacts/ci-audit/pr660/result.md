# CI Audit Gate - PR #660 / #689 Final Repair

- Date: 2026-07-25
- Worktree: `C:\001_AI\_codex_audit\Ludots-pr660-086d3f4-exact-20260724-1415`
- Branch: `codex/issues-649-651-ordering`
- Base checked: `origin/main` at `5712a4eef4cdb1011cc0694d52e77de95bfe4aaa`
- Remote PR head before this follow-up repair pass: `49a81360eb48d38e3f0b966e2926ee423328fcf8`
- Verified runtime repair head after this pass: `bd7ac14068db8fc66db13e4000d4aaed61cde031`
- Remote alignment verified after push: `refs/heads/codex/issues-649-651-ordering` == `refs/pull/660/head` == `bd7ac14068db8fc66db13e4000d4aaed61cde031`
- GitHub checks verified at `bd7ac14068db8fc66db13e4000d4aaed61cde031`: PASS for docs-governance, solution-verify, and camera-baseline; MassNavigation evidence recording was skipped by workflow definition.

## Gate Summary

Local final audit: PASS.

This file records the current repair scope, local gate evidence, and remote check result for the verified runtime repair head above. The PR body, issue #689, and `gh pr view 660` remain the SSOT for the latest PR head after evidence-only metadata refreshes. This artifact must not be read as evidence for older heads such as `086d3f4`, `b822b83`, `a829bdfe`, or `49a81360`.

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
- follow-up CI repair: explicit `MembershipTarget` spawn requests remain linked even when the spawned template/request does not author a `Team`
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
- `dotnet test src\Tests\GasTests\GasTests.csproj -c Debug --no-restore --filter "FullyQualifiedName~RuntimeEntitySpawnSystem_BatchTemplateExplicitMembershipWithoutTeam_LinksEverySpawnedEntity" --nologo --logger "console;verbosity=minimal"`: PASS, 1/1.
- `dotnet test src\Tests\GasTests\GasTests.csproj -c Debug --no-restore --filter "FullyQualifiedName~OrderCompositePlannerTests|FullyQualifiedName~OrderBufferSystem_BatchEntityAdmissionCapacityMiss" --nologo --logger "console;verbosity=minimal"`: PASS, 9/9.
- `dotnet test src\Tests\ArchitectureTests\ArchitectureTests.csproj -c Debug --no-restore --filter "FullyQualifiedName~GasAbilityExecHotPath_DoesNotCallWorldAddOrRemoveDirectly" --nologo --logger "console;verbosity=minimal"`: PASS, 1/1.
- `dotnet test src\Tests\PresentationTests\PresentationTests.csproj -c Debug --no-build --filter <MassNavigation PR acceptance presentationFilter> -v minimal`: PASS, 10/10.
- `git diff --check origin/main...HEAD`: PASS.
- `gh pr checks 660 --repo MightyBubble/Ludots` at `bd7ac14068db8fc66db13e4000d4aaed61cde031`: PASS for `validate`, `verify` / solution-verify, and `verify` / camera-baseline; evidence-recording job skipped by workflow definition.

## Merge State Note

GitHub reports `mergeable=true` / `mergeable=MERGEABLE` and `mergeable_state=blocked` / `mergeStateStatus=BLOCKED`. The status check rollup is `SUCCESS`; the observed blocker is repository merge policy (`OwnerOnlyWrites` ruleset), not a code or CI failure.

## Notes

Three unrelated artifact files were dirty before this repair and are intentionally excluded from this evidence commit:

- `artifacts/benchmarks/entity-query-tactics-showcase/benchmark-report.md`
- `artifacts/showcases/capability-standard-physics2d-stress/acceptance.md`
- `artifacts/showcases/capability-standard-physics2d-stress/keyframes.jsonl`

ci.audit.completed
