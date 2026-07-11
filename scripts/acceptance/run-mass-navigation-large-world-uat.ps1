param(
    [string]$OutputRoot = "",
    [int]$Iterations = 1,
    [string]$UntilLocalTime = "",
    [ValidateSet("raylib", "web")]
    [string]$Adapter = "raylib",
    [ValidateSet("auto", "always", "never")]
    [string]$Build = "auto",
    [ValidateRange(0, 36000)]
    [int]$PerformanceWarmupTicks = 300,
    [ValidateRange(1, 3600)]
    [int]$SteadyStateSeconds = 60,
    [switch]$MassNavigationTimingEnabled,
    [switch]$StopOnFailure
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$launcher = Join-Path $repoRoot "scripts\run-mod-launcher.cmd"
if (-not (Test-Path $launcher)) {
    throw "Launcher script not found: $launcher"
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\acceptance\mass-navigation-issue-642"
}
else {
    $OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$sessionRoot = Join-Path $OutputRoot (Join-Path "runs" (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force -Path $sessionRoot | Out-Null

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
$null | Set-Content -Path $summaryPath -Encoding UTF8 -NoNewline
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
    $timingEnabled = $RunRows.Count -gt 0 -and $RunRows[0].steady_timing_enabled_requested -eq $true
    $timingMode = if ($timingEnabled) { "timing-enabled" } else { "timing-disabled" }
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# MassNavigation Large-World UAT Soak")
    $lines.Add("")
    $lines.Add("## Scope")
    $lines.Add("- Target: ``MassNavigationMod``, the high-performance MassNavigation foundation acceptance surface.")
    $lines.Add("- Launcher path: ``scripts/run-mod-launcher.cmd cli launch '`$capability_standard_mass_navigation_large_world_10k' --adapter raylib --record ...``.")
    $lines.Add("- Evidence per run: ``battle-report.md``, ``trace.jsonl``, ``path.mmd``, ``summary.json``, ``visible-checklist.md``, and ``screens/timeline.png``.")
    $lines.Add("- Canonical latest successful run: ``artifacts/acceptance/mass-navigation-issue-642/{battle-report.md,trace.jsonl,path.mmd,summary.json}``.")
    $lines.Add("- Performance measurement: $timingMode MassNavigation diagnostics, disabled presentation system-breakdown timing, process-wide allocation/GC/working-set evidence, and solver-owned storage deltas.")
    $lines.Add("- Measurement scope is the full headless launcher process; allocation and working-set values are not presented as MassNavigation-only attribution.")
    $lines.Add("")
    $lines.Add("## Result")
    $lines.Add("- Output root: ``$Root``")
    $lines.Add("- Session runs: ``$sessionRoot``")
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
    $lines.Add("| 10K binding | Configured crowd binds through production ECS/runtime path | ``agent_count == 10000`` and ECS count matches |")
    $lines.Add("| Formal command source/order | Selected command-source agents enter OrderBuffer and move even if the short-lived group completes before the snapshot | non-zero submitted orders and moved command actors |")
    $lines.Add("| Complete health HUD | All 10K bars and 10K texts survive world-to-screen projection | exact bar/text counts and zero screen-HUD drops |")
    $lines.Add("| Camera/minimap residency | Remote minimap jump does not respawn/reset the scenario | stable agent/spawn/reset counts |")
    $lines.Add("| Avoidance | Central crowd overlap resolves through the production solver | final overlap/penetration checks |")
    $lines.Add("| Requested timing mode | The report matches the benchmark mode instead of hard-coding timing-disabled text | ``steady_timing_enabled_requested == $($timingEnabled.ToString().ToLowerInvariant())`` |")
    $lines.Add("| Capacity stability | Solver agent storage is prepared before the interval and does not grow | ``steady_capacity_growth_events == 0`` |")
    $lines.Add("| Memory evidence | Process-wide GC, retained heap and working set are reported without subsystem attribution | exact ``steady_*`` byte/count fields |")
    $lines.Add("")
    $lines.Add("## Runs")
    $lines.Add("| # | Result | Signature | Duration s | Ticks/orders | Avg tick ms | Alloc MB/s | Retained MB | WS growth MB | Peak WS MB | Capacity growth | Timeline |")
    $lines.Add("| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |")
    foreach ($run in $RunRows) {
        $result = if ($run.success) { "PASS" } else { "FAIL" }
        $signature = if ($run.normalized_signature) { $run.normalized_signature } else { "n/a" }
        $timeline = if ($run.timeline) { $run.timeline } else { "n/a" }
        $duration = if ($null -ne $run.steady_state_duration_seconds) { "{0:0.000}" -f [double]$run.steady_state_duration_seconds } else { "n/a" }
        $ticks = if ($null -ne $run.steady_tick_count -and $null -ne $run.steady_workload_order_count) { "$($run.steady_tick_count)/$($run.steady_workload_order_count)" } else { "n/a" }
        $averageTick = if ($null -ne $run.steady_average_tick_ms) { "{0:0.000}" -f [double]$run.steady_average_tick_ms } else { "n/a" }
        $allocated = if ($null -ne $run.steady_allocated_bytes_per_second) { "{0:0.00}" -f ([double]$run.steady_allocated_bytes_per_second / 1MB) } else { "n/a" }
        $retained = if ($null -ne $run.steady_retained_managed_growth_bytes) { "{0:0.00}" -f ([double]$run.steady_retained_managed_growth_bytes / 1MB) } else { "n/a" }
        $workingSet = if ($null -ne $run.steady_working_set_growth_bytes) { "{0:0.00}" -f ([double]$run.steady_working_set_growth_bytes / 1MB) } else { "n/a" }
        $peakWorkingSet = if ($null -ne $run.steady_peak_working_set_bytes) { "{0:0.00}" -f ([double]$run.steady_peak_working_set_bytes / 1MB) } else { "n/a" }
        $capacityGrowth = if ($null -ne $run.steady_capacity_growth_events) { [string]$run.steady_capacity_growth_events } else { "n/a" }
        $lines.Add("| $($run.run) | $result | ``$signature`` | $duration | $ticks | $averageTick | $allocated | $retained | $workingSet | $peakWorkingSet | $capacityGrowth | ``$timeline`` |")
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
    $runDir = Join-Path $sessionRoot ("run-{0:0000}" -f $runIndex)
    New-Item -ItemType Directory -Force -Path $runDir | Out-Null
    $logPath = Join-Path $runDir "run.log"
    $startedAt = Get-Date

    $argsList = @("cli", "launch", '$capability_standard_mass_navigation_large_world_10k', "--adapter", $Adapter)
    if (-not [string]::IsNullOrWhiteSpace($Build)) {
        $argsList += @("--build", $Build)
    }

    $argsList += @("--record", $runDir)

    $previousWarmupTicks = $env:LUDOTS_MASS_NAV_PERFORMANCE_WARMUP_TICKS
    $previousSteadyStateSeconds = $env:LUDOTS_MASS_NAV_STEADY_STATE_SECONDS
    $previousMassNavigationTimingEnabled = $env:LUDOTS_MASS_NAV_STEADY_TIMING_ENABLED
    $env:LUDOTS_MASS_NAV_PERFORMANCE_WARMUP_TICKS = [string]$PerformanceWarmupTicks
    $env:LUDOTS_MASS_NAV_STEADY_STATE_SECONDS = [string]$SteadyStateSeconds
    $env:LUDOTS_MASS_NAV_STEADY_TIMING_ENABLED = if ($MassNavigationTimingEnabled) { "true" } else { "false" }
    $previousErrorActionPreference = $ErrorActionPreference
    Push-Location $repoRoot
    try {
        # Windows PowerShell 5 wraps native stderr as ErrorRecord. Capture it in run.log and
        # decide success from the native exit code plus required evidence instead of terminating here.
        $ErrorActionPreference = "Continue"
        $output = & $launcher @argsList 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
        Pop-Location
        $env:LUDOTS_MASS_NAV_PERFORMANCE_WARMUP_TICKS = $previousWarmupTicks
        $env:LUDOTS_MASS_NAV_STEADY_STATE_SECONDS = $previousSteadyStateSeconds
        $env:LUDOTS_MASS_NAV_STEADY_TIMING_ENABLED = $previousMassNavigationTimingEnabled
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
        steady_state_duration_seconds = if ($summary) { $summary.steady_state_duration_seconds } else { $null }
        steady_tick_count = if ($summary) { $summary.steady_tick_count } else { $null }
        steady_workload_order_count = if ($summary) { $summary.steady_workload_order_count } else { $null }
        steady_average_tick_ms = if ($summary) { $summary.steady_average_tick_ms } else { $null }
        steady_max_tick_ms = if ($summary) { $summary.steady_max_tick_ms } else { $null }
        steady_timing_enabled_requested = if ($summary) { $summary.steady_timing_enabled_requested } else { $null }
        steady_timing_disabled = if ($summary) { $summary.steady_timing_disabled } else { $null }
        steady_total_allocated_bytes = if ($summary) { $summary.steady_total_allocated_bytes } else { $null }
        steady_allocated_bytes_per_second = if ($summary) { $summary.steady_allocated_bytes_per_second } else { $null }
        steady_allocated_bytes_per_tick = if ($summary) { $summary.steady_allocated_bytes_per_tick } else { $null }
        steady_retained_managed_growth_bytes = if ($summary) { $summary.steady_retained_managed_growth_bytes } else { $null }
        steady_gc_gen0_collections = if ($summary) { $summary.steady_gc_gen0_collections } else { $null }
        steady_gc_gen1_collections = if ($summary) { $summary.steady_gc_gen1_collections } else { $null }
        steady_gc_gen2_collections = if ($summary) { $summary.steady_gc_gen2_collections } else { $null }
        steady_working_set_growth_bytes = if ($summary) { $summary.steady_working_set_growth_bytes } else { $null }
        steady_peak_working_set_bytes = if ($summary) { $summary.steady_peak_working_set_bytes } else { $null }
        steady_capacity_growth_events = if ($summary) { $summary.steady_capacity_growth_events } else { $null }
        missing_evidence = $missingEvidence
        failed_checks = if ($summary) { @($summary.failed_checks) + @($missingEvidence | ForEach-Object { "missing evidence: $_" }) } else { @("missing summary.json or launcher failure") + @($missingEvidence | ForEach-Object { "missing evidence: $_" }) }
        log = $logPath
    }

    $runs.Add($row)
    Add-Content -Path $summaryPath -Value (ConvertTo-SafeJsonLine $row) -Encoding UTF8
    if ($success) {
        foreach ($artifactName in @("battle-report.md", "trace.jsonl", "path.mmd", "summary.json")) {
            Copy-Item -LiteralPath (Join-Path $runDir $artifactName) -Destination (Join-Path $OutputRoot $artifactName) -Force
        }
    }
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
