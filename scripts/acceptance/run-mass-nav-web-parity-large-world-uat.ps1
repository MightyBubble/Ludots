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
$launcher = Join-Path $repoRoot "scripts\run-mod-launcher.cmd"
if (-not (Test-Path $launcher)) {
    throw "Launcher script not found: $launcher"
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $OutputRoot = Join-Path $repoRoot "artifacts\acceptance\mass-nav-web-parity-large-world-soak-$stamp"
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
    $lines.Add("# MassNavWebParity Large-World UAT Soak")
    $lines.Add("")
    $lines.Add("## Scope")
    $lines.Add("- Target: ``MassNavWebParityMod``, the current high-performance mass-nav SSOT playground.")
    $lines.Add("- Launcher path: ``scripts/run-mod-launcher.cmd cli launch mass_nav_web_parity --adapter raylib --record ...``.")
    $lines.Add("- Evidence per run: ``battle-report.md``, ``trace.jsonl``, ``path.mmd``, ``summary.json``, ``visible-checklist.md``, and ``screens/timeline.png``.")
    $lines.Add("- No fallback contract: out-of-world commands must be rejected and counted; camera/minimap may inspect any in-bounds world coordinate.")
    $lines.Add("")
    $lines.Add("## Result")
    $lines.Add("- Output root: ``$Root``")
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
    $lines.Add("| Full minimap | Minimap starts as the whole world | full-world half extent check |")
    $lines.Add("| Camera jumps | Clicking minimap coordinates moves camera exactly there | 12 target tolerances, including all corners and empty space |")
    $lines.Add("| Formal selection | Box-selected units are represented by Ludots selection runtime | selected count after ``SelectionRuntime`` write |")
    $lines.Add("| GAS order | Right-click move goes through order buffer | active order/group after ``massNavMove`` |")
    $lines.Add("| Movement | Selected group actually moves | first, second, empty-world, multi-team, and near-edge command advance thresholds |")
    $lines.Add("| Reset hygiene | Reset clears selection/groups/orders | post-reset selected/groups zero |")
    $lines.Add("| Multi-team orders | Every dynamic team can own a selected move order | per-team command advance array |")
    $lines.Add("| Edge in-bounds | World edge is still normal playable RTS space | four corners plus four side midpoints are accepted and move |")
    $lines.Add("| Boundary errors | Invalid commands are diagnosed, not clamped | eight out-of-world probes, including just-over-edge cases, increment rejects |")
    $lines.Add("| World bounds | Runtime never leaks positions outside configured board | ``agents_outside_world == 0`` and solver/work area bounds checks |")
    $lines.Add("| Streaming/working set | Large-world active chunks stay live through soak | loaded chunk count remains positive |")
    $lines.Add("| Memory stability | Long run does not steadily leak | steady managed/allocated thresholds |")
    $lines.Add("")
    $lines.Add("## Runs")
    $lines.Add("| # | Result | Signature | First cm | Second cm | Empty cm | Multi-team min cm | Edge min cm | Steady managed MB | Steady alloc MB | Timeline |")
    $lines.Add("| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |")
    foreach ($run in $RunRows) {
        $result = if ($run.success) { "PASS" } else { "FAIL" }
        $signature = if ($run.normalized_signature) { $run.normalized_signature } else { "n/a" }
        $timeline = if ($run.timeline) { $run.timeline } else { "n/a" }
        $first = if ($null -ne $run.first_command_advance_cm) { "{0:0}" -f [double]$run.first_command_advance_cm } else { "n/a" }
        $second = if ($null -ne $run.second_command_advance_cm) { "{0:0}" -f [double]$run.second_command_advance_cm } else { "n/a" }
        $empty = if ($null -ne $run.empty_world_command_advance_cm) { "{0:0}" -f [double]$run.empty_world_command_advance_cm } else { "n/a" }
        $multiTeam = if ($null -ne $run.multi_team_min_advance_cm) { "{0:0}" -f [double]$run.multi_team_min_advance_cm } else { "n/a" }
        $edge = if ($null -ne $run.edge_inside_min_advance_cm) { "{0:0}" -f [double]$run.edge_inside_min_advance_cm } else { "n/a" }
        $steadyManaged = if ($null -ne $run.steady_managed_growth_bytes) { "{0:0.00}" -f ([double]$run.steady_managed_growth_bytes / 1MB) } else { "n/a" }
        $steadyAlloc = if ($null -ne $run.steady_allocated_bytes) { "{0:0.00}" -f ([double]$run.steady_allocated_bytes / 1MB) } else { "n/a" }
        $lines.Add("| $($run.run) | $result | ``$signature`` | $first | $second | $empty | $multiTeam | $edge | $steadyManaged | $steadyAlloc | ``$timeline`` |")
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

    $argsList = @("cli", "launch", "mass_nav_web_parity", "--adapter", $Adapter)
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
        first_command_advance_cm = if ($summary) { $summary.first_command_advance_cm } else { $null }
        second_command_advance_cm = if ($summary) { $summary.second_command_advance_cm } else { $null }
        empty_world_command_advance_cm = if ($summary) { $summary.empty_world_command_advance_cm } else { $null }
        edge_inside_command_advance_cm = if ($summary) { $summary.edge_inside_command_advance_cm } else { $null }
        multi_team_command_advance_cm = if ($summary) { $summary.multi_team_command_advance_cm } else { $null }
        multi_team_min_advance_cm = if ($summary) { $summary.multi_team_min_advance_cm } else { $null }
        edge_inside_min_advance_cm = if ($summary) { $summary.edge_inside_min_advance_cm } else { $null }
        final_loaded_chunks = if ($summary) { $summary.final_loaded_chunks } else { $null }
        final_rejects = if ($summary) { $summary.final_rejects } else { $null }
        boundary_rejects_added = if ($summary) { $summary.boundary_rejects_added } else { $null }
        steady_managed_growth_bytes = if ($summary) { $summary.steady_managed_growth_bytes } else { $null }
        steady_allocated_bytes = if ($summary) { $summary.steady_allocated_bytes } else { $null }
        missing_evidence = $missingEvidence
        failed_checks = if ($summary) { @($summary.failed_checks) + @($missingEvidence | ForEach-Object { "missing evidence: $_" }) } else { @("missing summary.json or launcher failure") + @($missingEvidence | ForEach-Object { "missing evidence: $_" }) }
        log = $logPath
    }

    $runs.Add($row)
    Add-Content -Path $summaryPath -Value (ConvertTo-SafeJsonLine $row) -Encoding UTF8
    Write-SoakReport -RunRows $runs -Path $reportPath -Root $OutputRoot -DeadlineValue $deadline

    if (-not $success -and $StopOnFailure) {
        throw "MassNavWebParity UAT soak failed at run $runIndex. See $runDir"
    }
}

Write-SoakReport -RunRows $runs -Path $reportPath -Root $OutputRoot -DeadlineValue $deadline
Write-Host "MassNavWebParity UAT soak complete."
Write-Host "output=$OutputRoot"
Write-Host "report=$reportPath"
Write-Host "summary=$summaryPath"
