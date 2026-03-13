param(
    [string]$Model = "sonnet"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$reviewDir = Join-Path $repoRoot "artifacts\reviews"
$outputPath = Join-Path $reviewDir "interaction-c3-any-unit-conditional-claude-review.md"
$promptPath = Join-Path $reviewDir "interaction-c3-any-unit-conditional-review-prompt.txt"

New-Item -ItemType Directory -Force -Path $reviewDir | Out-Null

$prompt = @"
You are doing a second-pass review of the Ludots C3 any-unit-conditional interaction showcase delivery.

Review the implementation and evidence against the scenario document `docs/architecture/interaction/features/unit_target/c3_any_unit_conditional.md`.

Focus on:
- bugs or behavioral mismatches
- evidence/code inconsistencies
- visual acceptance issues in the PNG screenshots, MP4, and GIF
- whether the delivery accurately states that the implementation uses `Search + targetDispatch payload` wrappers rather than direct explicit-target `relationFilter`
- whether the direct explicit-target relation-filter gap is documented honestly and linked to the tech-debt report
- whether the same ability really demonstrates hostile/friendly branching without cross-applying the wrong branch

Code to inspect:
- `mods/InteractionShowcaseMod/InteractionShowcaseIds.cs`
- `mods/InteractionShowcaseMod/InteractionShowcaseRuntimeKeys.cs`
- `mods/InteractionShowcaseMod/Systems/InteractionShowcaseAutoplaySystem.cs`
- `mods/InteractionShowcaseMod/Systems/InteractionShowcaseGasEventTapSystem.cs`
- `mods/InteractionShowcaseMod/Systems/InteractionShowcaseOverlaySystem.cs`
- `mods/InteractionShowcaseMod/assets/GAS/abilities.json`
- `mods/InteractionShowcaseMod/assets/GAS/effects.json`
- `mods/InteractionShowcaseMod/assets/Entities/templates.json`
- `mods/InteractionShowcaseMod/assets/Maps/interaction_c3_any_unit_conditional.json`
- `src/Tests/GasTests/C3AnyUnitConditionalTests.cs`
- `src/Tests/GasTests/Production/C3AnyUnitConditionalAcceptanceTests.cs`
- `src/Tools/Ludots.Launcher.Evidence/LauncherEvidenceRecorder.cs`
- `artifacts/techdebt/2026-03-13-c3-direct-explicit-target-relation-filter-gap.md`

Artifacts to inspect:
- `artifacts/acceptance/interaction-c3-any-unit-conditional/battle-report.md`
- `artifacts/acceptance/interaction-c3-any-unit-conditional/trace.jsonl`
- `artifacts/acceptance/interaction-c3-any-unit-conditional/path.mmd`
- `artifacts/acceptance/interaction-c3-any-unit-conditional/visual/battle-report.md`
- `artifacts/acceptance/interaction-c3-any-unit-conditional/visual/trace.jsonl`
- `artifacts/acceptance/interaction-c3-any-unit-conditional/visual/path.mmd`
- `artifacts/acceptance/interaction-c3-any-unit-conditional/visual/summary.json`
- `artifacts/acceptance/interaction-c3-any-unit-conditional/visual/visible-checklist.md`
- `artifacts/acceptance/interaction-c3-any-unit-conditional/visual/screens/000_start.png`
- `artifacts/acceptance/interaction-c3-any-unit-conditional/visual/screens/001_hostile_order_submitted.png`
- `artifacts/acceptance/interaction-c3-any-unit-conditional/visual/screens/002_hostile_polymorph_applied.png`
- `artifacts/acceptance/interaction-c3-any-unit-conditional/visual/screens/003_friendly_order_submitted.png`
- `artifacts/acceptance/interaction-c3-any-unit-conditional/visual/screens/004_friendly_haste_applied.png`
- `artifacts/acceptance/interaction-c3-any-unit-conditional/visual/screens/timeline.png`
- `artifacts/acceptance/interaction-c3-any-unit-conditional/visual/interaction-c3-any-unit-conditional.mp4`
- `artifacts/acceptance/interaction-c3-any-unit-conditional/visual/interaction-c3-any-unit-conditional.gif`

Return markdown with:
1. Findings first, ordered by severity, with file references when relevant.
2. Residual risks.
3. Final verdict.

If there are no findings, say `No findings.` explicitly before residual risks.
"@

$prompt | Set-Content -Path $promptPath -Encoding UTF8

Push-Location $repoRoot
try {
    $review = claude -p --model $Model --add-dir $repoRoot --permission-mode bypassPermissions --dangerously-skip-permissions $prompt
    if ($LASTEXITCODE -ne 0) {
        throw "Claude review failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

$review | Set-Content -Path $outputPath -Encoding UTF8

Write-Host "Claude review saved."
Write-Host "  Prompt: $promptPath"
Write-Host "  Review: $outputPath"
