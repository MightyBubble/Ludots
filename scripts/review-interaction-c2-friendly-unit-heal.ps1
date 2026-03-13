param(
    [string]$Model = "sonnet"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$reviewDir = Join-Path $repoRoot "artifacts\reviews"
$outputPath = Join-Path $reviewDir "interaction-c2-friendly-unit-heal-claude-review.md"
$promptPath = Join-Path $reviewDir "interaction-c2-friendly-unit-heal-review-prompt.txt"

New-Item -ItemType Directory -Force -Path $reviewDir | Out-Null

$prompt = @"
You are doing a second-pass review of the Ludots C2 friendly unit heal interaction showcase delivery.

Review the implementation and evidence against the scenario document `docs/architecture/interaction/features/unit_target/c2_friendly_unit_heal.md`.

Focus on:
- bugs or behavioral mismatches
- evidence/code inconsistencies
- visual acceptance issues in the PNG screenshots, MP4, and GIF
- missing guard branches or missing follow-up risks
- whether the doc accurately states that the negative branches are showcase-local validation unless the code proves a native GAS rejection path

Code to inspect:
- `mods/InteractionShowcaseMod/InteractionShowcaseIds.cs`
- `mods/InteractionShowcaseMod/InteractionShowcaseRuntimeKeys.cs`
- `mods/InteractionShowcaseMod/InteractionShowcaseModEntry.cs`
- `mods/InteractionShowcaseMod/Systems/InteractionShowcaseAutoplaySystem.cs`
- `mods/InteractionShowcaseMod/Systems/InteractionShowcaseGasEventTapSystem.cs`
- `mods/InteractionShowcaseMod/Systems/InteractionShowcaseOverlaySystem.cs`
- `mods/InteractionShowcaseMod/assets/GAS/abilities.json`
- `mods/InteractionShowcaseMod/assets/GAS/effects.json`
- `mods/InteractionShowcaseMod/assets/Entities/templates.json`
- `mods/InteractionShowcaseMod/assets/Maps/interaction_c2_friendly_unit_heal.json`
- `src/Tests/GasTests/C2FriendlyUnitHealTests.cs`
- `src/Tests/GasTests/Production/C2FriendlyUnitHealAcceptanceTests.cs`
- `src/Tools/Ludots.Launcher.Evidence/LauncherEvidenceRecorder.cs`

Artifacts to inspect:
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/battle-report.md`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/trace.jsonl`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/path.mmd`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/visual/battle-report.md`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/visual/trace.jsonl`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/visual/path.mmd`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/visual/summary.json`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/visual/visible-checklist.md`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/visual/screens/000_start.png`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/visual/screens/001_order_submitted.png`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/visual/screens/002_heal_applied.png`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/visual/screens/003_hostile_target_blocked.png`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/visual/screens/004_dead_ally_blocked.png`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/visual/screens/timeline.png`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/visual/interaction-c2-friendly-unit-heal.mp4`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/visual/interaction-c2-friendly-unit-heal.gif`

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
