param(
    [string]$OutputRoot = "",
    [int]$Iterations = 1,
    [string]$UntilLocalTime = "",
    [ValidateSet("raylib", "web")]
    [string]$Adapter = "raylib",
    [string]$Build = "",
    [switch]$StopOnFailure
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$sourceSha = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceSha -notmatch '^[0-9a-f]{40}$') {
    throw "Could not resolve the repository HEAD for MassNavigation evidence."
}
$launcher = Join-Path $repoRoot "scripts\run-mod-launcher.cmd"
if (-not (Test-Path $launcher)) {
    throw "Launcher script not found: $launcher"
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $OutputRoot = Join-Path $repoRoot "artifacts\acceptance\mass-navigation-large-world-soak-$stamp"
}
else {
    $OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$deadline = $null
if (-not [string]::IsNullOrWhiteSpace($UntilLocalTime)) {
    $parsed = [DateTime]::MinValue
    if (-not [DateTime]::TryParse($UntilLocalTime, [ref]$parsed)) {
        throw "UntilLocalTime must be parseable by PowerShell DateTime, for example '06:00' or '2026-04-27 06:00'."
    }

    $now = Get-Date
    if ($UntilLocalTime -match '^\d{1,2}:\d{2}$') {
        $deadline = Get-Date -Year $now.Year -Month $now.Month -Day $now.Day -Hour $parsed.Hour -Minute $parsed.Minute -Second 0
        if ($deadline -le $now) {
            $deadline = $deadline.AddDays(1)
        }
    }
    else {
        $deadline = $parsed
    }
}

if ($Iterations -lt 0) {
    throw "Iterations must be 0 or greater. Use 0 with UntilLocalTime for an overnight run."
}

if ($Iterations -eq 0 -and $null -eq $deadline) {
    throw "Iterations=0 requires UntilLocalTime so the soak has an explicit stop condition."
}

$summaryPath = Join-Path $OutputRoot "soak-summary.jsonl"
$reportPath = Join-Path $OutputRoot "soak-report.md"
$runs = New-Object System.Collections.Generic.List[object]

function ConvertTo-SafeJsonLine {
    param([object]$Value)
    return ($Value | ConvertTo-Json -Depth 12 -Compress)
}

function Write-SoakReport {
    param(
        [System.Collections.Generic.List[object]]$RunRows,
        [string]$Path,
        [string]$Root,
        [object]$DeadlineValue
    )

    $passed = @($RunRows | Where-Object { $_.success -eq $true }).Count
    $failed = @($RunRows | Where-Object { $_.success -ne $true }).Count
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# MassNavigation Large-World UAT Soak")
    $lines.Add("")
    $lines.Add("## Scope")
    $lines.Add("- Target: ``MassNavigationMod``, the high-performance MassNavigation foundation acceptance surface.")
    $lines.Add("- Launcher path: ``scripts/run-mod-launcher.cmd cli launch mass_navigation --adapter raylib --record ...``.")
    $lines.Add("- Evidence per run: ``battle-report.md``, ``trace.jsonl``, ``path.mmd``, ``summary.json``, ``visible-checklist.md``, and ``screens/timeline.png``.")
    $lines.Add("- No fallback contract: missing payload, transform, emission, culling, projection, capacity, or source-SHA evidence fails the run.")
    $lines.Add("")
    $lines.Add("## Result")
    $lines.Add("- Output root: ``$Root``")
    $lines.Add("- Source HEAD: ``$sourceSha``")
    $lines.Add("- Deadline: ``$DeadlineValue``")
    $lines.Add("- Runs: ``$($RunRows.Count)``")
    $lines.Add("- Passed: ``$passed``")
    $lines.Add("- Failed: ``$failed``")
    $lines.Add("")
    $lines.Add("## UAT Matrix")
    $lines.Add("| Case | Player-facing expectation | Machine check |")
    $lines.Add("| --- | --- | --- |")
    $lines.Add("| 64km world boot | Designer sees one standard RTS battlefield | ``world_width_cm == 6400000 && world_height_cm == 6400000`` |")
    $lines.Add("| Four dynamic teams | Scenario is not a hard-coded two-team demo | ``teams >= 4`` |")
    $lines.Add("| Formal order chain | The evidence harness fills CommandSource, then one shared move reaches every member through OrderQueue and OrderBuffer | command source and active move-order counts are positive; real box-drag/right-click gestures are covered by the production-path test |")
    $lines.Add("| Anchor chain | Models and HUD anchors follow the same navigation result | 64 fixed solver/ECS/VisualTransform/performer-root samples stay within tolerance |")
    $lines.Add("| World HUD emission | Every authored agent emits a bar and text item | world bar/text counts cover all agents |")
    $lines.Add("| Screen HUD projection | Visible world HUD reaches the host screen buffer | screen bar/text counts are positive and projection failures are zero |")
    $lines.Add("| Capacity | Large crowds do not disappear silently | WorldHud, ScreenHud and minimap drops are all zero |")
    $lines.Add("| Stage diagnosis | A missing visible result names the broken stage | payload/transform/emission/culling/projection/capacity failures are separately counted |")
    $lines.Add("| Full minimap | Minimap remains the whole-world RTS view | marker count covers the scenario and drop count is zero |")
    $lines.Add("| Camera travel | Player can inspect a remote hot zone and return | camera displacement exceeds 5km and agent/spawn counts remain stable |")
    $lines.Add("")
    $lines.Add("## Runs")
    $lines.Add("| # | Result | Source SHA | Signature | World HUD b/t/drop | Screen HUD b/t/drop | Stage failures | Timeline |")
    $lines.Add("| --- | --- | --- | --- | --- | --- | --- | --- |")
    foreach ($run in $RunRows) {
        $result = if ($run.success) { "PASS" } else { "FAIL" }
        $signature = if ($run.normalized_signature) { $run.normalized_signature } else { "n/a" }
        $timeline = if ($run.timeline) { $run.timeline } else { "n/a" }
        $worldHud = "$($run.world_hud_bar_count)/$($run.world_hud_text_count)/$($run.world_hud_dropped_total)"
        $screenHud = "$($run.screen_hud_bar_count)/$($run.screen_hud_text_count)/$($run.screen_hud_dropped_total)"
        $stageFailures = "$($run.payload_failure_count)/$($run.transform_failure_count)/$($run.emission_failure_count)/$($run.culling_failure_count)/$($run.projection_failure_count)/$($run.capacity_failure_count)"
        $lines.Add("| $($run.run) | $result | ``$($run.source_sha)`` | ``$signature`` | $worldHud | $screenHud | $stageFailures | ``$timeline`` |")
    }

    $lines.Add("")
    $lines.Add("## Failure Handling")
    $lines.Add("- If any run fails, inspect that run directory first; it keeps stdout/stderr in ``run.log`` and the last evidence screenshots.")
    $lines.Add("- Do not treat a missing service or missing summary as a fallback success. Missing evidence is a failed run.")
    $lines.Add("- Headless evidence does not claim live render FPS. Use the Raylib HUD or a renderer benchmark for real FPS.")
    Set-Content -Path $Path -Value $lines -Encoding UTF8
}

$runIndex = 0
while ($true) {
    if ($Iterations -gt 0 -and $runIndex -ge $Iterations) {
        break
    }

    if ($null -ne $deadline -and (Get-Date) -ge $deadline) {
        break
    }

    $runIndex++
    $runDir = Join-Path $OutputRoot ("run-{0:0000}" -f $runIndex)
    New-Item -ItemType Directory -Force -Path $runDir | Out-Null
    $logPath = Join-Path $runDir "run.log"
    $startedAt = Get-Date

    $argsList = @("cli", "launch", "mass_navigation", "--adapter", $Adapter)
    if (-not [string]::IsNullOrWhiteSpace($Build)) {
        $argsList += @("--build", $Build)
    }

    $argsList += @("--record", $runDir)

    Push-Location $repoRoot
    try {
        $output = & $launcher @argsList 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    $endedAt = Get-Date
    $output | Set-Content -Path $logPath -Encoding UTF8
    $summaryFile = Join-Path $runDir "summary.json"
    $summary = $null
    $success = $false
    if ($exitCode -eq 0 -and (Test-Path $summaryFile)) {
        $summary = Get-Content -Path $summaryFile -Raw -Encoding UTF8 | ConvertFrom-Json
        $success = [bool]$summary.success
        if ($summary.source_sha -ne $sourceSha) {
            $success = $false
        }
    }

    $requiredEvidence = @(
        "battle-report.md",
        "trace.jsonl",
        "path.mmd",
        "summary.json",
        "visible-checklist.md",
        "screens\timeline.png"
    )
    $missingEvidence = @(
        $requiredEvidence |
            Where-Object { -not (Test-Path (Join-Path $runDir $_)) }
    )
    if ($missingEvidence.Count -gt 0) {
        $success = $false
    }

    $row = [pscustomobject]@{
        run = $runIndex
        success = $success
        exit_code = $exitCode
        started_at = $startedAt.ToString("o")
        ended_at = $endedAt.ToString("o")
        duration_seconds = [math]::Round(($endedAt - $startedAt).TotalSeconds, 3)
        output_dir = $runDir
        battle_report = (Join-Path $runDir "battle-report.md")
        trace = (Join-Path $runDir "trace.jsonl")
        summary = $summaryFile
        timeline = (Join-Path $runDir "screens\timeline.png")
        normalized_signature = if ($summary) { $summary.normalized_signature } else { $null }
        source_sha = if ($summary) { $summary.source_sha } else { $null }
        world_hud_bar_count = if ($summary) { $summary.world_hud_bar_count } else { $null }
        world_hud_text_count = if ($summary) { $summary.world_hud_text_count } else { $null }
        world_hud_dropped_total = if ($summary) { $summary.world_hud_dropped_total } else { $null }
        screen_hud_bar_count = if ($summary) { $summary.screen_hud_bar_count } else { $null }
        screen_hud_text_count = if ($summary) { $summary.screen_hud_text_count } else { $null }
        screen_hud_dropped_total = if ($summary) { $summary.screen_hud_dropped_total } else { $null }
        payload_failure_count = if ($summary) { $summary.payload_failure_count } else { $null }
        transform_failure_count = if ($summary) { $summary.transform_failure_count } else { $null }
        emission_failure_count = if ($summary) { $summary.emission_failure_count } else { $null }
        culling_failure_count = if ($summary) { $summary.culling_failure_count } else { $null }
        projection_failure_count = if ($summary) { $summary.projection_failure_count } else { $null }
        capacity_failure_count = if ($summary) { $summary.capacity_failure_count } else { $null }
        missing_evidence = $missingEvidence
        failed_checks = if ($summary) { @($summary.failed_checks) + @($missingEvidence | ForEach-Object { "missing evidence: $_" }) + @(if ($summary.source_sha -ne $sourceSha) { "source SHA mismatch: expected $sourceSha, got $($summary.source_sha)" }) } else { @("missing summary.json or launcher failure") + @($missingEvidence | ForEach-Object { "missing evidence: $_" }) }
        log = $logPath
    }

    $runs.Add($row)
    Add-Content -Path $summaryPath -Value (ConvertTo-SafeJsonLine $row) -Encoding UTF8
    Write-SoakReport -RunRows $runs -Path $reportPath -Root $OutputRoot -DeadlineValue $deadline

    if (-not $success -and $StopOnFailure) {
        throw "MassNavigation UAT soak failed at run $runIndex. See $runDir"
    }
}

Write-SoakReport -RunRows $runs -Path $reportPath -Root $OutputRoot -DeadlineValue $deadline
Write-Host "MassNavigation UAT soak complete."
Write-Host "output=$OutputRoot"
Write-Host "report=$reportPath"
Write-Host "summary=$summaryPath"
Write-Host "source_sha=$sourceSha"
