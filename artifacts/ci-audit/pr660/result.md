# CI Audit Gate - PR #660 / #689 Transaction Closeout

- Date: 2026-07-25
- Worktree: `C:\001_AI\_codex_audit\Ludots-pr660-086d3f4-exact-20260724-1415`
- Branch: `codex/issues-649-651-ordering`
- Base checked: `origin/main` at `5712a4eef4cdb1011cc0694d52e77de95bfe4aaa`
- PR head before this repair: `ffb352fdb58318279518f9d9b366ce6c304d2b6a`
- Verified runtime repair commit: `2819ea59e33cd87fdc21bdcf56f48f5fc010e9d8`
- Exact pushed PR head and remote checks are recorded after push in PR #660 and issue #689. This artifact intentionally does not claim a post-push remote result before the push exists.

## Gate Summary

Local repair audit: PASS for commit `2819ea59e33cd87fdc21bdcf56f48f5fc010e9d8`.

This evidence supersedes the older `bd7ac14068db8fc66db13e4000d4aaed61cde031` and `ffb352fdb58318279518f9d9b366ce6c304d2b6a` closeout text. Those commits are historical context only; they are not the current completion claim for PR #660.

## Scope

This pass closes the current audit blockers:

- AbilityExec terminal-capacity preflight now happens before finish/fail/interrupt state mutation, including natural timeline exhaustion.
- AbilityExec missing effect/event/graph services and invalid explicit targets fail typed execution instead of silently succeeding or falling back.
- AbilityExec caller params fail fast when fixed capacity is exceeded.
- OrderContinuation retries no longer advance the processed cursor before successful submit.
- Same-action aiming by a different actor is rejected and does not overwrite the existing aiming session.
- Entity command panel activation results are recorded and surfaced instead of being discarded by click handlers.

## Evidence

- `dotnet test src\Tests\ArchitectureTests\ArchitectureTests.csproj -c Debug --no-restore --nologo --logger "console;verbosity=minimal"`: PASS, 188/188.
- `dotnet test src\Tests\GasTests\GasTests.csproj -c Debug --no-restore --filter "FullyQualifiedName~InputOrderAbilityAuditTests" --nologo --logger "console;verbosity=minimal"`: PASS, 95/95.
- `dotnet test src\Tests\GasTests\GasTests.csproj -c Debug --no-restore --filter "FullyQualifiedName~InputOrderContractTests|FullyQualifiedName~CollectionGasEntityCommandPanelAggregationTests" --nologo --logger "console;verbosity=minimal"`: PASS, 56/56.
- `dotnet test src\Tests\GasTests\GasTests.csproj -c Debug --no-restore --filter "FullyQualifiedName~RoadNetworkShowcaseTests|FullyQualifiedName~OrderCompositePlannerTests" --nologo --logger "console;verbosity=minimal"`: PASS, 49/49.
- `dotnet test src\Tests\GasTests\GasTests.csproj -c Debug --no-restore --filter "FullyQualifiedName~AbilityExecInteractionContextTests|FullyQualifiedName~AbilityExecLoaderFailFastTests|FullyQualifiedName~GasExecutionBudgetTests" --nologo --logger "console;verbosity=minimal"`: PASS, 73/73.
- `dotnet test src\Tests\GasTests\GasTests.csproj -c Debug --no-restore --filter "FullyQualifiedName~AbilityExecInteractionContextTests|FullyQualifiedName~InputOrderAbilityAuditTests" --nologo --logger "console;verbosity=minimal"`: PASS, 105/105 after the final AbilityExec start-failure tightening.
- `git diff --check`: PASS.

## Notes

Three unrelated artifact files were dirty before this repair and are intentionally excluded from this PR #660 closeout evidence:

- `artifacts/benchmarks/entity-query-tactics-showcase/benchmark-report.md`
- `artifacts/showcases/capability-standard-physics2d-stress/acceptance.md`
- `artifacts/showcases/capability-standard-physics2d-stress/keyframes.jsonl`

ci.audit.completed
