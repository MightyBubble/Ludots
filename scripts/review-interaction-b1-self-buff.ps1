param(
    [string]$Model = "sonnet"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$reviewDir = Join-Path $repoRoot "artifacts\reviews"
$outputPath = Join-Path $reviewDir "interaction-b1-self-buff-claude-review.md"
$promptPath = Join-Path $reviewDir "interaction-b1-self-buff-review-prompt.txt"

New-Item -ItemType Directory -Force -Path $reviewDir | Out-Null

$prompt = @"
You are doing a second-pass review of the Ludots B1 self buff interaction showcase delivery.

Review the implementation and evidence against the scenario document `docs/architecture/interaction/features/instant_press/b1_self_buff.md`.

Focus on:
- bugs or behavioral mismatches
- evidence/code inconsistencies
- visual acceptance issues in the PNG screenshots
- missing guard branches or missing follow-up risks

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
- `src/Tests/GasTests/Production/InteractionShowcaseAcceptanceTests.cs`
- `src/Tests/GasTests/SelfBuffTests.cs`
- `src/Tools/Ludots.Launcher.Evidence/LauncherEvidenceRecorder.cs`

Artifacts to inspect:
- `artifacts/acceptance/interaction-b1-self-buff/battle-report.md`
- `artifacts/acceptance/interaction-b1-self-buff/trace.jsonl`
- `artifacts/acceptance/interaction-b1-self-buff/path.mmd`
- `artifacts/acceptance/interaction-b1-self-buff/visual/battle-report.md`
- `artifacts/acceptance/interaction-b1-self-buff/visual/summary.json`
- `artifacts/acceptance/interaction-b1-self-buff/visual/visible-checklist.md`
- `artifacts/acceptance/interaction-b1-self-buff/visual/screens/000_start.png`
- `artifacts/acceptance/interaction-b1-self-buff/visual/screens/001_order_submitted.png`
- `artifacts/acceptance/interaction-b1-self-buff/visual/screens/002_buff_active.png`
- `artifacts/acceptance/interaction-b1-self-buff/visual/screens/003_buff_expired.png`
- `artifacts/acceptance/interaction-b1-self-buff/visual/screens/004_silenced_blocked.png`
- `artifacts/acceptance/interaction-b1-self-buff/visual/screens/005_insufficient_mana.png`
- `artifacts/acceptance/interaction-b1-self-buff/visual/screens/timeline.png`
- `artifacts/acceptance/interaction-b1-self-buff/visual/interaction-b1-self-buff.mp4`
- `artifacts/acceptance/interaction-b1-self-buff/visual/interaction-b1-self-buff.gif`
- `artifacts/techdebt/2026-03-13-gas-tag-effective-state-grant-mismatch.md`

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
