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

function Get-SourceSha {
    $sha = (& git -C $repoRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $sha -notmatch '^[0-9a-f]{40}$') {
        throw "Could not resolve the repository HEAD for MassNavigation evidence."
    }

    return $sha
}

function Assert-CleanSourceTree {
    param([string]$Stage)
    $dirty = @(& git -C $repoRoot status --porcelain)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect the repository worktree at $Stage."
    }

    if ($dirty.Count -gt 0) {
        throw "MassNavigation evidence requires a clean worktree at $Stage. Commit or remove local changes before recording."
    }
}

function Test-FiniteJsonNumber {
    param([object]$Value)
    if ($null -eq $Value) {
        return $false
    }

    $typeCode = [System.Type]::GetTypeCode($Value.GetType())
    $isJsonNumber = $typeCode -in @(
        [System.TypeCode]::Byte,
        [System.TypeCode]::SByte,
        [System.TypeCode]::UInt16,
        [System.TypeCode]::UInt32,
        [System.TypeCode]::UInt64,
        [System.TypeCode]::Int16,
        [System.TypeCode]::Int32,
        [System.TypeCode]::Int64,
        [System.TypeCode]::Decimal,
        [System.TypeCode]::Double,
        [System.TypeCode]::Single
    )
    if (-not $isJsonNumber) {
        return $false
    }

    try {
        $number = [double]$Value
        return -not [double]::IsNaN($number) -and -not [double]::IsInfinity($number)
    }
    catch {
        return $false
    }
}

function Get-PointDistanceSquared {
    param([object]$Left, [object]$Right)
    $dx = ([double]$Left.x_cm) - ([double]$Right.x_cm)
    $dy = ([double]$Left.y_cm) - ([double]$Right.y_cm)
    return ($dx * $dx) + ($dy * $dy)
}

function Test-MassNavigationSummaryContract {
    param(
        [object]$Summary,
        [string]$ExpectedAdapter,
        [string]$ExpectedSourceSha
    )

    $failures = New-Object System.Collections.Generic.List[string]
    if ($Summary.scenario -ne "mass_navigation_large_world") {
        $failures.Add("scenario mismatch: expected mass_navigation_large_world, got $($Summary.scenario)")
    }
    if ($Summary.adapter -ne $ExpectedAdapter) {
        $failures.Add("adapter mismatch: expected $ExpectedAdapter, got $($Summary.adapter)")
    }
    if ($Summary.source_sha -ne $ExpectedSourceSha) {
        $failures.Add("source SHA mismatch: expected $ExpectedSourceSha, got $($Summary.source_sha)")
    }

    $selectors = @($Summary.selectors)
    if ($selectors.Count -ne 1 -or $selectors[0] -ne '$capability_standard_mass_navigation_large_world_10k') {
        $failures.Add("selector mismatch: expected only `$capability_standard_mass_navigation_large_world_10k")
    }
    $rootMods = @($Summary.root_mods)
    if ($rootMods.Count -ne 1 -or $rootMods[0] -ne "CapabilityStandardMassNavigationLargeWorld10kMod") {
        $failures.Add("root mod mismatch: expected only CapabilityStandardMassNavigationLargeWorld10kMod")
    }

    $samples = @($Summary.anchor_samples)
    if ($samples.Count -ne 64) {
        $failures.Add("anchor sample count mismatch: expected 64, got $($samples.Count)")
    }

    if (-not (Test-FiniteJsonNumber $Summary.movement_sample_threshold_cm) -or [double]$Summary.movement_sample_threshold_cm -le 0) {
        $failures.Add("movement sample threshold is missing or invalid")
    }
    if (-not (Test-FiniteJsonNumber $Summary.first_command_moved_sample_count) -or [int]$Summary.first_command_moved_sample_count -lt 8) {
        $failures.Add("first command moved sample count too low: $($Summary.first_command_moved_sample_count)")
    }
    if (-not (Test-FiniteJsonNumber $Summary.second_command_moved_sample_count) -or [int]$Summary.second_command_moved_sample_count -lt 8) {
        $failures.Add("second command moved sample count too low: $($Summary.second_command_moved_sample_count)")
    }
    if (-not (Test-FiniteJsonNumber $Summary.first_command_max_sample_displacement_cm) -or
        [double]$Summary.first_command_max_sample_displacement_cm -lt [double]$Summary.movement_sample_threshold_cm) {
        $failures.Add("first command max sample displacement is below threshold: $($Summary.first_command_max_sample_displacement_cm)")
    }
    if (-not (Test-FiniteJsonNumber $Summary.second_command_max_sample_displacement_cm) -or
        [double]$Summary.second_command_max_sample_displacement_cm -lt [double]$Summary.movement_sample_threshold_cm) {
        $failures.Add("second command max sample displacement is below threshold: $($Summary.second_command_max_sample_displacement_cm)")
    }

    $stageFailureFields = @(
        "payload_failure_count",
        "transform_failure_count",
        "emission_failure_count",
        "culling_failure_count",
        "projection_failure_count",
        "capacity_failure_count"
    )
    foreach ($field in $stageFailureFields) {
        if (-not (Test-FiniteJsonNumber $Summary.$field)) {
            $failures.Add("stage failure field is missing or invalid: $field")
        }
        elseif ([int]$Summary.$field -ne 0) {
            $failures.Add("stage failure field must be zero across the full timeline: $field=$($Summary.$field)")
        }
    }

    $performanceFields = @(
        "max_frame_ms",
        "max_mass_navigation_ms",
        "max_mass_navigation_prepare_ms",
        "max_mass_navigation_steer_ms",
        "max_mass_navigation_resolve_ms",
        "max_mass_navigation_crowd_step_ms",
        "max_mass_navigation_sync_ms"
    )
    foreach ($field in $performanceFields) {
        if (-not (Test-FiniteJsonNumber $Summary.$field) -or [double]$Summary.$field -lt 0) {
            $failures.Add("performance field is missing or invalid: $field")
        }
    }

    if (-not (Test-FiniteJsonNumber $Summary.avoidance_max_visible_agent_count) -or [int]$Summary.avoidance_max_visible_agent_count -le 0) {
        $failures.Add("visible crowd count is missing or zero")
    }

    $pointNames = @("solver_world_cm", "ecs_world_cm", "visual_world_cm", "performer_world_cm")
    for ($sampleIndex = 0; $sampleIndex -lt $samples.Count; $sampleIndex++) {
        $sample = $samples[$sampleIndex]
        $validPoints = $true
        foreach ($pointName in $pointNames) {
            $point = $sample.$pointName
            if ($null -eq $point -or
                -not (Test-FiniteJsonNumber $point.x_cm) -or
                -not (Test-FiniteJsonNumber $point.y_cm)) {
                $failures.Add("anchor sample $sampleIndex has unreadable $pointName x_cm/y_cm")
                $validPoints = $false
            }
        }

        if ($validPoints) {
            $toleranceSq = 25.0 * 25.0
            if ((Get-PointDistanceSquared $sample.solver_world_cm $sample.ecs_world_cm) -gt $toleranceSq) {
                $failures.Add("anchor sample $sampleIndex solver->ECS distance exceeds 25cm")
            }
            if ((Get-PointDistanceSquared $sample.ecs_world_cm $sample.visual_world_cm) -gt $toleranceSq) {
                $failures.Add("anchor sample $sampleIndex ECS->visual distance exceeds 25cm")
            }
            if ((Get-PointDistanceSquared $sample.visual_world_cm $sample.performer_world_cm) -gt $toleranceSq) {
                $failures.Add("anchor sample $sampleIndex visual->performer distance exceeds 25cm")
            }
        }
    }

    return @($failures)
}

Assert-CleanSourceTree -Stage "recording start"
$sourceSha = Get-SourceSha
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
    $lines.Add("| Formal order chain | The evidence harness fills CommandSource, then two shared moves reach every member through OrderQueue and formal OrderBuffer activation | command source and active move-order counts are positive; sampled units move after both logical commands; real box-drag/right-click gestures are covered by the production-path test |")
    $lines.Add("| Commanded movement | Units visibly respond to both logical commands instead of only accepting orders | ``first_command_moved_sample_count >= 8`` and ``second_command_moved_sample_count >= 8`` |")
    $lines.Add("| Position chain | Models and HUD roots follow the same navigation result | 64 fixed solver/ECS/VisualTransform/performer-root samples stay within tolerance |")
    $lines.Add("| World HUD emission | Every authored agent emits a bar and text item | world bar/text counts cover all agents |")
    $lines.Add("| Screen HUD projection | Visible world HUD reaches the host screen buffer | screen bar/text counts are positive and projection failures are zero |")
    $lines.Add("| Capacity | Large crowds do not disappear silently | WorldHud, ScreenHud and minimap drops are all zero |")
    $lines.Add("| Stage diagnosis | A missing visible result names the broken stage | payload/transform/emission/culling/projection/capacity failures are separately counted |")
    $lines.Add("| Full minimap | Minimap remains the whole-world RTS view | marker count covers the scenario and drop count is zero |")
    $lines.Add("| Camera travel | Player can inspect a remote hot zone and return | camera displacement exceeds 5km and agent/spawn counts remain stable |")
    $lines.Add("")
    $lines.Add("## Runs")
    $lines.Add("| # | Result | Source SHA | Signature | Movement samples | World HUD b/t/drop | Screen HUD b/t/drop | Stage failures | Perf max frame/mass/prepare/steer/resolve/crowd/sync ms | Timeline |")
    $lines.Add("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |")
    foreach ($run in $RunRows) {
        $result = if ($run.success) { "PASS" } else { "FAIL" }
        $signature = if ($run.normalized_signature) { $run.normalized_signature } else { "n/a" }
        $timeline = if ($run.timeline) { $run.timeline } else { "n/a" }
        $worldHud = "$($run.world_hud_bar_count)/$($run.world_hud_text_count)/$($run.world_hud_dropped_total)"
        $screenHud = "$($run.screen_hud_bar_count)/$($run.screen_hud_text_count)/$($run.screen_hud_dropped_total)"
        $movement = "$($run.first_command_moved_sample_count)/$($run.second_command_moved_sample_count)"
        $stageFailures = "$($run.payload_failure_count)/$($run.transform_failure_count)/$($run.emission_failure_count)/$($run.culling_failure_count)/$($run.projection_failure_count)/$($run.capacity_failure_count)"
        $perf = "$($run.max_frame_ms)/$($run.max_mass_navigation_ms)/$($run.max_mass_navigation_prepare_ms)/$($run.max_mass_navigation_steer_ms)/$($run.max_mass_navigation_resolve_ms)/$($run.max_mass_navigation_crowd_step_ms)/$($run.max_mass_navigation_sync_ms)"
        $lines.Add("| $($run.run) | $result | ``$($run.source_sha)`` | ``$signature`` | $movement | $worldHud | $screenHud | $stageFailures | $perf | ``$timeline`` |")
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

    $argsList = @("cli", "launch", "capability_standard_mass_navigation_large_world_10k", "--adapter", $Adapter)
    if (-not [string]::IsNullOrWhiteSpace($Build)) {
        $argsList += @("--build", $Build)
    }

    $argsList += @("--record", $runDir)

    Push-Location $repoRoot
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $output = & $launcher @argsList 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
        Pop-Location
    }

    $endedAt = Get-Date
    $output | Set-Content -Path $logPath -Encoding UTF8
    $summaryFile = Join-Path $runDir "summary.json"
    $summary = $null
    $success = $false
    $summaryValidationFailures = @()
    $sourceStateFailures = New-Object System.Collections.Generic.List[string]
    $postRunSha = Get-SourceSha
    if ($postRunSha -ne $sourceSha) {
        $sourceStateFailures.Add("repository HEAD changed during evidence run: expected $sourceSha, got $postRunSha")
    }
    $postRunDirty = @(& git -C $repoRoot status --porcelain)
    if ($LASTEXITCODE -ne 0) {
        $sourceStateFailures.Add("could not inspect repository cleanliness after evidence run")
    }
    elseif ($postRunDirty.Count -gt 0) {
        $sourceStateFailures.Add("repository worktree became dirty during evidence run")
    }
    if ($exitCode -eq 0 -and (Test-Path $summaryFile)) {
        $summary = Get-Content -Path $summaryFile -Raw -Encoding UTF8 | ConvertFrom-Json
        $success = [bool]$summary.success
        $summaryValidationFailures = @(Test-MassNavigationSummaryContract -Summary $summary -ExpectedAdapter $Adapter -ExpectedSourceSha $sourceSha)
        if ($summaryValidationFailures.Count -gt 0 -or $sourceStateFailures.Count -gt 0) {
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
        first_command_moved_sample_count = if ($summary) { $summary.first_command_moved_sample_count } else { $null }
        second_command_moved_sample_count = if ($summary) { $summary.second_command_moved_sample_count } else { $null }
        payload_failure_count = if ($summary) { $summary.payload_failure_count } else { $null }
        transform_failure_count = if ($summary) { $summary.transform_failure_count } else { $null }
        emission_failure_count = if ($summary) { $summary.emission_failure_count } else { $null }
        culling_failure_count = if ($summary) { $summary.culling_failure_count } else { $null }
        projection_failure_count = if ($summary) { $summary.projection_failure_count } else { $null }
        capacity_failure_count = if ($summary) { $summary.capacity_failure_count } else { $null }
        max_frame_ms = if ($summary) { $summary.max_frame_ms } else { $null }
        max_mass_navigation_ms = if ($summary) { $summary.max_mass_navigation_ms } else { $null }
        max_mass_navigation_prepare_ms = if ($summary) { $summary.max_mass_navigation_prepare_ms } else { $null }
        max_mass_navigation_steer_ms = if ($summary) { $summary.max_mass_navigation_steer_ms } else { $null }
        max_mass_navigation_resolve_ms = if ($summary) { $summary.max_mass_navigation_resolve_ms } else { $null }
        max_mass_navigation_crowd_step_ms = if ($summary) { $summary.max_mass_navigation_crowd_step_ms } else { $null }
        max_mass_navigation_sync_ms = if ($summary) { $summary.max_mass_navigation_sync_ms } else { $null }
        missing_evidence = $missingEvidence
        failed_checks = if ($summary) { @($summary.failed_checks) + @($summaryValidationFailures) + @($sourceStateFailures) + @($missingEvidence | ForEach-Object { "missing evidence: $_" }) } else { @("missing summary.json or launcher failure") + @($sourceStateFailures) + @($missingEvidence | ForEach-Object { "missing evidence: $_" }) }
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

$failedRuns = @($runs | Where-Object { $_.success -ne $true }).Count
if ($failedRuns -gt 0) {
    Write-Error "MassNavigation UAT soak failed: $failedRuns run(s) failed. See $reportPath"
    exit 1
}
