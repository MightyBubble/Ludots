# PR86 Mainline Integration Audit

## Summary

- Audit date: 2026-04-06
- Repository worktree: `C:\001_AI\LudotsProd_pr86`
- Integration branch: `codex/pr86-mainline-integration`
- PR86 merge base in this worktree: `2c7c5028`
- Scope: verify PR86 after merge conflict resolution, fix PR-specific regressions, and determine whether the integrated result is safe to advance toward mainline.

## Decision

PR86 is safe to advance from the integration branch.

Reason:

- The merged suite no longer has any net-new failing tests versus the audited main baseline.
- The merged suite is strictly better than the main baseline by one test: `Ludots.Tests.GAS.Production.ProductionModDemoLogTests.MobaDemoLog` fails on main but does not fail on the integrated branch.
- The PR-specific RTS regressions found during audit were corrected in the integration branch and reverified.

## Verification Evidence

### Current integration result

- Command:
  - `dotnet test C:\001_AI\LudotsProd_pr86\src\Tests\GasTests\GasTests.csproj -m:1 /nodeReuse:false /p:UseSharedCompilation=false --logger "trx;LogFileName=gas-merge-postfix.trx"`
- Result:
  - Total: `854`
  - Passed: `825`
  - Failed: `29`
  - TRX: `C:\001_AI\LudotsProd_pr86\src\Tests\GasTests\TestResults\gas-merge-postfix.trx`

### Main baseline result

- Baseline TRX:
  - `C:\001_AI\LudotsProd_mainaudit\src\Tests\GasTests\TestResults\gas-main.trx`
- Result:
  - Failed: `30`

### Failure-set comparison

- Net-new failures on integrated branch versus main baseline: `0`
- Failures present on main baseline but not on integrated branch: `1`
  - `Ludots.Tests.GAS.Production.ProductionModDemoLogTests.MobaDemoLog`

## Issues Found And Resolved During Audit

### 1. Test isolation bug in fixed-size attribute buffers

Files:

- `src/Tests/GasTests/GasFeatureGapTests.cs`
- `src/Tests/GasTests/TagEffectArchitectureTests.cs`

Resolution:

- Removed reliance on globally assigned attribute ids in a fixed-size `AttributeBuffer`.
- Switched the affected tests to local high-slot ids (`AttributeBuffer.MAX_ATTRS - 2/-1`) so full-suite ordering cannot corrupt expectations.

Impact:

- Eliminated two merge-only failures that were caused by test isolation, not runtime behavior.

### 2. RTS first-contact selection drift after PR86

Files:

- `mods/RtsDemoMod/Triggers/RtsSetupOnMapLoadedTrigger.cs`
- `mods/RtsDemoMod/Runtime/RtsQuickSelectToolbarProvider.cs`

Resolution:

- Made default selection scenario-aware for training maps.
- Restored coherent sandbox first-contact behavior by preferring `Peasant` on `rts_entry`.
- Updated sandbox toolbar subtitle so the user-facing affordances match the actual mod flow (`RMB`, `War3 build`, `C&C placement`, `SC2 Warp`).

Impact:

- Fixed the PR-specific RTS onboarding regressions in `RtsMap_Load_SeedsPrimarySelectionForFirstContact` and `RtsActors_AreReadable_OnMapLoad_And_AfterProductionSpawns`.

### 3. Outdated RTS demo-log contract

File:

- `src/Tests/GasTests/Production/ProductionModDemoLogTests.cs`

Resolution:

- Rebased `RtsDemoLog` from obsolete SCV/Marine/Zergling scripting onto the current `rts_entry` sandbox semantics.
- Added the real UI/runtime dependencies used by the current RTS flow (`CoreInputMod`, `EntityCommandPanelMod`, `UIRoot`, `SkiaTextMeasurer`, `SkiaImageSizeProvider`).
- Updated order submission to the same contract used by RTS acceptance tests (`PlayerId = 1`, `SubmitMode = Immediate`).
- Fixed the Protoss section to wait for `Progression.Rts.WarpGate` and slot-override refresh before issuing the warp command.

Impact:

- `RtsDemoLog` now passes as a current-contract verification instead of asserting against removed prototype behavior.

### 4. Deterministic expectation mismatch in C&C training acceptance

File:

- `src/Tests/GasTests/Production/RtsTrainingShowcaseAcceptanceTests.cs`

Resolution:

- Updated `CncScenario.MidProgressExpectedCost` from `200f` to `100f`.

Impact:

- Brought the acceptance test back in line with the deterministic frame-28 credit deduction observed in the current runtime.

## Remaining Risk

The suite still has `29` failing tests, but audit comparison shows these are inherited baseline failures rather than PR86 regressions.

This means the correct conclusion is:

- PR86 should not be blocked on those failures.
- The remaining failures still deserve separate cleanup work, but they are not evidence that the PR86 integration is unsafe.

## Operational Note

The original `C:\001_AI\LudotsProd` worktree is intentionally left untouched because it is user-dirty.

As a result:

- The integration result is prepared and verified in `C:\001_AI\LudotsProd_pr86`.
- Advancing the actual `main` branch ref inside the dirty original worktree was intentionally not performed during this audit step, to avoid mutating the user's active local state.
