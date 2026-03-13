param(
    [string]$Model = "sonnet"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$reviewDir = Join-Path $repoRoot "artifacts\reviews"
$outputPath = Join-Path $reviewDir "interaction-c1-hostile-unit-damage-claude-review.md"
$promptPath = Join-Path $reviewDir "interaction-c1-hostile-unit-damage-review-prompt.txt"

New-Item -ItemType Directory -Force -Path $reviewDir | Out-Null

$prompt = @"
You are doing a second-pass review of the Ludots C1 hostile unit damage interaction showcase delivery.

Review the implementation and evidence against the scenario document `docs/architecture/interaction/features/unit_target/c1_hostile_unit_damage.md`.

Focus on:
- bugs or behavioral mismatches
- evidence/code inconsistencies
- visual acceptance issues in the PNG screenshots, MP4, and GIF
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
- `mods/InteractionShowcaseMod/assets/GAS/graphs.json`
- `mods/InteractionShowcaseMod/assets/Entities/templates.json`
- `mods/InteractionShowcaseMod/assets/Maps/interaction_c1_hostile_unit_damage.json`
- `src/Tests/GasTests/C1HostileUnitDamageTests.cs`
- `src/Tests/GasTests/Production/C1HostileUnitDamageAcceptanceTests.cs`
- `src/Tools/Ludots.Launcher.Evidence/LauncherEvidenceRecorder.cs`

Artifacts to inspect:
- `artifacts/acceptance/interaction-c1-hostile-unit-damage/battle-report.md`
- `artifacts/acceptance/interaction-c1-hostile-unit-damage/trace.jsonl`
- `artifacts/acceptance/interaction-c1-hostile-unit-damage/path.mmd`
- `artifacts/acceptance/interaction-c1-hostile-unit-damage/visual/battle-report.md`
- `artifacts/acceptance/interaction-c1-hostile-unit-damage/visual/summary.json`
- `artifacts/acceptance/interaction-c1-hostile-unit-damage/visual/visible-checklist.md`
- `artifacts/acceptance/interaction-c1-hostile-unit-damage/visual/screens/000_start.png`
- `artifacts/acceptance/interaction-c1-hostile-unit-damage/visual/screens/001_order_submitted.png`
- `artifacts/acceptance/interaction-c1-hostile-unit-damage/visual/screens/002_damage_applied.png`
- `artifacts/acceptance/interaction-c1-hostile-unit-damage/visual/screens/003_invalid_target_blocked.png`
- `artifacts/acceptance/interaction-c1-hostile-unit-damage/visual/screens/004_out_of_range_blocked.png`
- `artifacts/acceptance/interaction-c1-hostile-unit-damage/visual/screens/timeline.png`
- `artifacts/acceptance/interaction-c1-hostile-unit-damage/visual/interaction-c1-hostile-unit-damage.mp4`
- `artifacts/acceptance/interaction-c1-hostile-unit-damage/visual/interaction-c1-hostile-unit-damage.gif`

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
