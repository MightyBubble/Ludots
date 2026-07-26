param(
    [ValidateSet("auto", "always", "never")]
    [string]$Build = "auto",

    # Optional strict selectors. Empty = full 2x3 matrix. No silent fallbacks.
    [string]$Scene = "",
    [string]$Algorithm = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$launcherCli = Join-Path $repoRoot "src\Tools\Ludots.Launcher.Cli\bin\Release\net8.0\Ludots.Launcher.Cli.exe"
$launcherCliProject = Join-Path $repoRoot "src\Tools\Ludots.Launcher.Cli\Ludots.Launcher.Cli.csproj"
$raylibAdapterProject = Join-Path $repoRoot "src\Adapters\Raylib\Ludots.Adapter.Raylib\Ludots.Adapter.Raylib.csproj"
$raylibAppProject = Join-Path $repoRoot "src\Apps\Raylib\Ludots.App.Raylib\Ludots.App.Raylib.csproj"
$dynamicNavBakeShowcaseProject = Join-Path $repoRoot "mods\showcases\nav_bake\DynamicNavBakeShowcaseMod\DynamicNavBakeShowcaseMod.csproj"
$rtsShowcaseProject = Join-Path $repoRoot "mods\showcases\nav_bake\NavBakeDynamicRtsShowcaseMod\NavBakeDynamicRtsShowcaseMod.csproj"
$openWorldShowcaseProject = Join-Path $repoRoot "mods\showcases\nav_bake\NavBakeOpenWorld64x64ShowcaseMod\NavBakeOpenWorld64x64ShowcaseMod.csproj"

$ValidScenes = @("rts", "open_world")
$ValidAlgorithms = @("recast", "cdt", "layered-span")

function Clear-DynamicNavBakeAutoEnv {
    @(
        "LUDOTS_DYNAMIC_NAV_BAKE_AUTO_TIMELINE",
        "LUDOTS_TAKE_SCREENSHOT_PATH",
        "LUDOTS_TAKE_SCREENSHOT_FRAMES",
        "LUDOTS_TAKE_SCREENSHOT_FRAME",
        "LUDOTS_AUTO_EXIT_FRAME",
        "LUDOTS_RAYLIB_DIAGNOSTIC_PATH",
        "LUDOTS_RAYLIB_DETERMINISTIC_FRAME_DELTA",
        "LUDOTS_MIN_RUNTIME_MS_BEFORE_SCREENSHOT"
    ) | ForEach-Object {
        Remove-Item "Env:$_" -ErrorAction SilentlyContinue
    }
}

function Assert-FreshNonEmptyFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][datetime]$NotBeforeUtc,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Label is missing: $Path"
    }

    $item = Get-Item -LiteralPath $Path
    if ($item.Length -le 0) {
        throw "$Label is empty: $Path"
    }

    if ($item.LastWriteTimeUtc -lt $NotBeforeUtc) {
        throw "$Label was not freshly created by this run: $Path (LastWriteTimeUtc=$($item.LastWriteTimeUtc.ToString('o')), required>=$($NotBeforeUtc.ToString('o')))"
    }
}

function Get-FileSha256Hex {
    param([Parameter(Mandatory = $true)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-RaylibAutoTimeline {
    param([Parameter(Mandatory = $true)][string]$ConfigPath)

    if (-not (Test-Path -LiteralPath $ConfigPath)) {
        throw "DynamicNavBakeShowcaseConfig.json not found: $ConfigPath"
    }

    $json = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
    if ($null -eq $json.raylibAutoTimeline) {
        throw "Config missing required raylibAutoTimeline section: $ConfigPath"
    }

    $timeline = $json.raylibAutoTimeline
    foreach ($name in @(
            "algorithmRequestEarliestFrame",
            "algorithmCommitDeadlineFrame",
            "initialScreenshotFrame",
            "dynamicActionFrame",
            "dynamicCommitDeadlineFrame",
            "dynamicScreenshotFrame",
            "finalActionFrame",
            "finalCommitDeadlineFrame",
            "finalScreenshotFrame",
            "autoExitFrame",
            "cameraTargetToleranceCm",
            "requiredQuiescentFixedTicks",
            "finalCaptureCompletionMode",
            "finalArrivalMemberToleranceCm",
            "finalArrivalRequiredStableFixedTicks",
            "playerFraming")) {
        if ($null -eq $timeline.$name) {
            throw "Config raylibAutoTimeline missing '$name': $ConfigPath"
        }
    }

    $framing = $timeline.playerFraming
    foreach ($name in @(
            "captureWidthPx",
            "captureHeightPx",
            "safeInsetLeftPx",
            "safeInsetTopPx",
            "safeInsetRightPx",
            "safeInsetBottomPx",
            "marginCm",
            "minDistanceCm",
            "maxDistanceCm",
            "baseDistanceCm",
            "minSquadMembersOnScreen",
            "minProjectedSquadSpanPx",
            "pathLookaheadCm",
            "coverageBuffer",
            "distanceToleranceCm")) {
        if ($null -eq $framing.$name) {
            throw "Config raylibAutoTimeline.playerFraming missing '$name': $ConfigPath"
        }
    }

    if ([int]$timeline.requiredQuiescentFixedTicks -lt 2) {
        throw "Config raylibAutoTimeline.requiredQuiescentFixedTicks must be >= 2: $ConfigPath"
    }

    return $timeline
}

function Get-SequencedScreenshotPath {
    param(
        [Parameter(Mandatory = $true)][string]$BasePath,
        [Parameter(Mandatory = $true)][int]$SequenceIndexZeroBased,
        [Parameter(Mandatory = $true)][int]$Frame
    )

    $directory = Split-Path -Parent $BasePath
    $extension = [System.IO.Path]::GetExtension($BasePath)
    if ([string]::IsNullOrWhiteSpace($extension)) {
        $extension = ".png"
    }

    $fileName = [System.IO.Path]::GetFileNameWithoutExtension($BasePath)
    if ([string]::IsNullOrWhiteSpace($fileName)) {
        $fileName = "screenshot"
    }

    $sequenced = "{0}_{1:000}_f{2:0000}{3}" -f $fileName, ($SequenceIndexZeroBased + 1), $Frame, $extension
    if ([string]::IsNullOrWhiteSpace($directory)) {
        return [System.IO.Path]::GetFullPath($sequenced)
    }

    return Join-Path $directory $sequenced
}

function Assert-DiagnosticHealthy {
    param([Parameter(Mandatory = $true)][string]$DiagnosticPath)

    if (-not (Test-Path -LiteralPath $DiagnosticPath)) {
        throw "Diagnostic log is missing: $DiagnosticPath"
    }

    $text = Get-Content -LiteralPath $DiagnosticPath -Raw
    if ([string]::IsNullOrWhiteSpace($text)) {
        throw "Diagnostic log is empty: $DiagnosticPath"
    }

    $lower = $text.ToLowerInvariant()
    if ($lower.Contains("unhandled exception") -or $lower.Contains("exception in game loop")) {
        throw "Diagnostic indicates unhandled exception: $DiagnosticPath"
    }

    if ($lower -match '(^|\r?\n)[^\r\n]*\bfatal\b') {
        throw "Diagnostic indicates fatal content: $DiagnosticPath"
    }

    if ($lower -match '(^|\r?\n)[^\r\n]*\berror\b') {
        throw "Diagnostic indicates error content: $DiagnosticPath"
    }
}

function Assert-ScreenshotEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$DiagnosticPath,
        [Parameter(Mandatory = $true)][int[]]$ExpectedFrames,
        [Parameter(Mandatory = $true)][string[]]$ScreenshotPaths
    )

    if ($ExpectedFrames.Count -ne 3 -or $ScreenshotPaths.Count -ne 3) {
        throw "Screenshot evidence requires exactly 3 frames and 3 screenshot paths."
    }

    $lines = Get-Content -LiteralPath $DiagnosticPath
    for ($i = 0; $i -lt $ExpectedFrames.Count; $i++) {
        $frame = $ExpectedFrames[$i]
        $shotMarker = "screenshot frame=$frame"
        $shotIndexes = @()
        for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
            if ($lines[$lineIndex] -like "*$shotMarker*") {
                $shotIndexes += $lineIndex
            }
        }

        if ($shotIndexes.Count -le 0) {
            throw "Diagnostic missing matching screenshot marker '$shotMarker': $DiagnosticPath"
        }

        $visibleOk = $false
        foreach ($shotIndex in $shotIndexes) {
            $start = [Math]::Max(0, $shotIndex - 3)
            $end = [Math]::Min($lines.Count - 1, $shotIndex + 3)
            for ($j = $start; $j -le $end; $j++) {
                if ($lines[$j] -match 'visibleEntities=(\d+)') {
                    $visible = [int]$Matches[1]
                    if ($visible -gt 0) {
                        $visibleOk = $true
                        break
                    }
                }
            }

            if ($visibleOk) {
                break
            }
        }

        if (-not $visibleOk) {
            throw "Screenshot-adjacent timing for frame=$frame must show visibleEntities > 0: $DiagnosticPath"
        }
    }

    $hashes = @()
    foreach ($path in $ScreenshotPaths) {
        $hashes += (Get-FileSha256Hex -Path $path)
    }

    $unique = $hashes | Select-Object -Unique
    if ($unique.Count -ne 3) {
        throw "Expected three distinct screenshot SHA256 hashes, got $($unique.Count): $($hashes -join ', ')"
    }
}

function Get-ProjectReleaseArtifactPath {
    param([Parameter(Mandatory = $true)][string]$ProjectPath)

    $projectDir = (Resolve-Path (Split-Path -Parent $ProjectPath)).Path
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($ProjectPath)
    $extension = ".dll"
    if ($projectName -eq "Ludots.Launcher.Cli") {
        $extension = ".exe"
    }

    $modsRoot = (Resolve-Path (Join-Path $repoRoot "mods")).Path.TrimEnd('\') + '\'
    $relativeOutput = if ($projectDir.StartsWith($modsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        # mods/Directory.Build.props owns the shared Mod output convention.
        "bin\net8.0"
    }
    else {
        "bin\Release\net8.0"
    }

    return Join-Path (Join-Path $projectDir $relativeOutput) ($projectName + $extension)
}

function Assert-RequiredReleaseArtifact {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$FailurePrefix
    )

    $artifact = Get-ProjectReleaseArtifactPath -ProjectPath $ProjectPath
    if (-not (Test-Path -LiteralPath $artifact -PathType Leaf)) {
        throw "${FailurePrefix}: $artifact (from $ProjectPath)"
    }

    if ((Get-Item -LiteralPath $artifact).Length -le 0) {
        throw "$FailurePrefix because artifact is empty: $artifact (from $ProjectPath)"
    }

    return $artifact
}

function Get-RequiredReleaseProjects {
    param([Parameter(Mandatory = $true)][string[]]$SceneKeys)

    $projects = New-Object System.Collections.Generic.List[string]
    $projects.Add($launcherCliProject)
    $projects.Add($raylibAdapterProject)
    $projects.Add($raylibAppProject)
    $projects.Add($dynamicNavBakeShowcaseProject)

    $uniqueScenes = @($SceneKeys | Select-Object -Unique)
    foreach ($sceneKey in $uniqueScenes) {
        if ($sceneKey -eq "rts") {
            $projects.Add($rtsShowcaseProject)
        }
        elseif ($sceneKey -eq "open_world") {
            $projects.Add($openWorldShowcaseProject)
        }
        else {
            throw "Unsupported scene key for Release artifact resolution: '$sceneKey'."
        }
    }

    return @($projects)
}

function Resolve-NewestDotNet9Sdk {
    $dotnetCommand = Get-Command dotnet -ErrorAction Stop
    $dotnetHostPath = $dotnetCommand.Source
    if ([string]::IsNullOrWhiteSpace($dotnetHostPath) -or -not (Test-Path -LiteralPath $dotnetHostPath)) {
        throw "dotnet host executable was not resolved from PATH."
    }

    $listOutput = & $dotnetHostPath --list-sdks 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet --list-sdks failed (exit=$LASTEXITCODE): $($listOutput | Out-String)"
    }

    $lines = @($listOutput | ForEach-Object { "$_" } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($lines.Count -le 0) {
        throw "dotnet --list-sdks returned no SDK entries."
    }

    $best = $null
    foreach ($line in $lines) {
        if ($line -notmatch '^(?<version>\S+)\s+\[(?<root>.+)\]\s*$') {
            throw "Malformed dotnet --list-sdks line: '$line'"
        }

        $versionText = $Matches["version"]
        $sdkRoot = $Matches["root"].Trim()
        if ([string]::IsNullOrWhiteSpace($sdkRoot)) {
            throw "Malformed dotnet --list-sdks line (empty SDK root): '$line'"
        }

        if ($versionText -notmatch '^9\.') {
            continue
        }

        if ($versionText -notmatch '^(?<numeric>\d+\.\d+\.\d+)') {
            throw "Malformed .NET 9 SDK version '$versionText' in line: '$line'"
        }

        $numericVersion = [version]$Matches["numeric"]
        $dotnetDllPath = Join-Path (Join-Path $sdkRoot $versionText) "dotnet.dll"
        $candidate = [pscustomobject]@{
            VersionText = $versionText
            NumericVersion = $numericVersion
            DotnetDllPath = $dotnetDllPath
            DotnetHostPath = $dotnetHostPath
        }

        if ($null -eq $best) {
            $best = $candidate
            continue
        }

        if ($candidate.NumericVersion -gt $best.NumericVersion) {
            $best = $candidate
            continue
        }

        if ($candidate.NumericVersion -eq $best.NumericVersion -and
            [string]::CompareOrdinal($candidate.VersionText, $best.VersionText) -gt 0) {
            $best = $candidate
        }
    }

    if ($null -eq $best) {
        throw "No .NET 9 SDK found in dotnet --list-sdks output. Install a 9.x SDK or set PATH to a host that reports one."
    }

    if (-not (Test-Path -LiteralPath $best.DotnetDllPath)) {
        throw "Resolved .NET 9 SDK dotnet.dll is missing: $($best.DotnetDllPath)"
    }

    Write-Host "Using .NET 9 SDK $($best.VersionText) via $($best.DotnetDllPath)"
    return $best
}

function Ensure-ReleaseBuild {
    param(
        [Parameter(Mandatory = $true)][string]$Mode,
        [Parameter(Mandatory = $true)][string[]]$Projects
    )

    if ($Projects.Count -le 0) {
        throw "Ensure-ReleaseBuild requires at least one project."
    }

    foreach ($project in $Projects) {
        if (-not (Test-Path -LiteralPath $project)) {
            throw "Required project missing: $project"
        }
    }

    if ($Mode -eq "never") {
        foreach ($project in $Projects) {
            Assert-RequiredReleaseArtifact `
                -ProjectPath $project `
                -FailurePrefix "Build=never but required Release artifact is missing for selected run" | Out-Null
        }

        if (-not (Test-Path -LiteralPath $launcherCli)) {
            throw "Build=never but Launcher CLI is missing: $launcherCli"
        }

        return
    }

    $sdk = Resolve-NewestDotNet9Sdk

    # `dotnet build` is incremental. Running it for both auto/always keeps --build never
    # launches tied to current sources instead of treating an old Launcher exe as sufficient.
    Write-Host "Building Release Dynamic NavBake Raylib acceptance dependencies (mode=$Mode) with SDK $($sdk.VersionText) (-m:1)..."
    foreach ($project in $Projects) {
        & $sdk.DotnetHostPath $sdk.DotnetDllPath build $project -c Release --verbosity minimal -m:1
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed for $project (exit=$LASTEXITCODE)."
        }

        Assert-RequiredReleaseArtifact `
            -ProjectPath $project `
            -FailurePrefix "Release build did not produce required artifact" | Out-Null
    }

    if (-not (Test-Path -LiteralPath $launcherCli)) {
        throw "Release Launcher CLI was not produced: $launcherCli"
    }
}

function Invoke-DynamicNavBakeRaylibRun {
    param(
        [Parameter(Mandatory = $true)][string]$PresetId,
        [Parameter(Mandatory = $true)][string]$Algorithm,
        [Parameter(Mandatory = $true)][string]$ArtifactRoot,
        [Parameter(Mandatory = $true)]$Timeline
    )

    $screensDir = Join-Path $ArtifactRoot "screens"
    New-Item -ItemType Directory -Force -Path $screensDir | Out-Null
    New-Item -ItemType Directory -Force -Path $ArtifactRoot | Out-Null

    $screenshotBase = Join-Path $screensDir ($Algorithm + ".png")
    $diagnosticPath = Join-Path $ArtifactRoot ($Algorithm + ".diagnostic.log")
    $launchLogPath = Join-Path $ArtifactRoot ($Algorithm + ".launch.log")

    $frameList = @(
        [int]$Timeline.initialScreenshotFrame,
        [int]$Timeline.dynamicScreenshotFrame,
        [int]$Timeline.finalScreenshotFrame
    )
    $expectedScreenshots = @(
        (Get-SequencedScreenshotPath -BasePath $screenshotBase -SequenceIndexZeroBased 0 -Frame $frameList[0]),
        (Get-SequencedScreenshotPath -BasePath $screenshotBase -SequenceIndexZeroBased 1 -Frame $frameList[1]),
        (Get-SequencedScreenshotPath -BasePath $screenshotBase -SequenceIndexZeroBased 2 -Frame $frameList[2])
    )

    Get-ChildItem -LiteralPath $screensDir -Filter ("${Algorithm}_*.png") -File -ErrorAction SilentlyContinue |
        Remove-Item -Force
    foreach ($path in (@($diagnosticPath, $launchLogPath) + $expectedScreenshots)) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force
        }
    }

    $runStartedUtc = [datetime]::UtcNow
    Clear-DynamicNavBakeAutoEnv
    try {
        $env:LUDOTS_DYNAMIC_NAV_BAKE_AUTO_TIMELINE = $Algorithm
        $env:LUDOTS_TAKE_SCREENSHOT_PATH = $screenshotBase
        $env:LUDOTS_TAKE_SCREENSHOT_FRAMES = ($frameList -join ",")
        $env:LUDOTS_AUTO_EXIT_FRAME = ([int]$Timeline.autoExitFrame).ToString()
        $env:LUDOTS_RAYLIB_DIAGNOSTIC_PATH = $diagnosticPath
        $env:LUDOTS_RAYLIB_DETERMINISTIC_FRAME_DELTA = "true"

        Write-Host "Launching preset=$PresetId algorithm=$Algorithm ..."
        Push-Location $repoRoot
        try {
            # Invoke the Release Launcher CLI exe directly. Do not use scripts/run-mod-launcher.cmd:
            # that wrapper unconditionally rebuilds Launcher before every launch.
            $previousErrorActionPreference = $ErrorActionPreference
            $ErrorActionPreference = "Continue"
            try {
                $launchOutput = & $launcherCli launch "preset:$PresetId" --adapter raylib --build never 2>&1
                $launcherExitCode = $LASTEXITCODE
            }
            finally {
                $ErrorActionPreference = $previousErrorActionPreference
            }

            $launchOutput | ForEach-Object { $_ }
            $launchOutput | Set-Content -LiteralPath $launchLogPath -Encoding utf8
            if ($launcherExitCode -ne 0) {
                throw "Launcher failed for preset=$PresetId algorithm=$Algorithm (exit=$launcherExitCode). See $launchLogPath"
            }
        }
        finally {
            Pop-Location
        }

        $pidLine = $launchOutput |
            Where-Object { $_ -is [string] -and $_ -match '^pid=(\d+)$' } |
            Select-Object -First 1
        if ($null -eq $pidLine) {
            throw "Launcher did not report pid= for preset=$PresetId algorithm=$Algorithm. See $launchLogPath"
        }

        $targetPid = [int]($pidLine -replace '^pid=', '')
        $timeoutMs = 900 * 1000
        # Launcher may report pid= after the child already exited (crash/fast auto-exit).
        # Match relationship-showcase: SilentlyContinue, then either wait or continue to evidence gates.
        $targetProcess = Get-Process -Id $targetPid -ErrorAction SilentlyContinue
        if ($null -ne $targetProcess) {
            $exited = $targetProcess.WaitForExit($timeoutMs)
            if (-not $exited) {
                try { Stop-Process -Id $targetPid -Force -ErrorAction SilentlyContinue } catch { }
                throw "Raylib acceptance run timed out after 900s (preset=$PresetId algorithm=$Algorithm pid=$targetPid)."
            }

            try {
                $exitCode = $targetProcess.ExitCode
            }
            catch {
                $exitCode = $null
            }

            if ($null -ne $exitCode -and $exitCode -ne 0) {
                throw "Raylib process exited nonzero ($exitCode) for preset=$PresetId algorithm=$Algorithm. See $diagnosticPath / $launchLogPath"
            }

            if ($null -eq $exitCode) {
                Write-Host "Process pid=$targetPid exited but ExitCode was unavailable; requiring diagnostic health + auto-exit marker + fresh screenshots."
            }
        }
        else {
            Write-Host "Process pid=$targetPid already exited before wait; requiring diagnostic health + auto-exit marker + fresh screenshots."
        }

        Assert-DiagnosticHealthy -DiagnosticPath $diagnosticPath
        if (-not (Select-String -LiteralPath $diagnosticPath -Pattern 'auto-exit frame=' -SimpleMatch -Quiet)) {
            throw "Diagnostic missing auto-exit marker for preset=$PresetId algorithm=${Algorithm}: $diagnosticPath"
        }

        for ($i = 0; $i -lt $expectedScreenshots.Count; $i++) {
            Assert-FreshNonEmptyFile -Path $expectedScreenshots[$i] -NotBeforeUtc $runStartedUtc -Label "Screenshot[$i]"
        }

        Assert-ScreenshotEvidence `
            -DiagnosticPath $diagnosticPath `
            -ExpectedFrames $frameList `
            -ScreenshotPaths $expectedScreenshots

        Write-Host "OK preset=$PresetId algorithm=$Algorithm"
        Write-Host "  screens: $($expectedScreenshots -join ', ')"
    }
    finally {
        Clear-DynamicNavBakeAutoEnv
    }
}

$hasScene = -not [string]::IsNullOrWhiteSpace($Scene)
$hasAlgorithm = -not [string]::IsNullOrWhiteSpace($Algorithm)
if ($hasScene -ne $hasAlgorithm) {
    throw "Selectors require both -Scene and -Algorithm together (or neither for the full 2x3 matrix)."
}

if ($hasScene -and ($ValidScenes -notcontains $Scene)) {
    throw "Invalid -Scene '$Scene'. Allowed: $($ValidScenes -join ', ')."
}

if ($hasAlgorithm -and ($ValidAlgorithms -notcontains $Algorithm)) {
    throw "Invalid -Algorithm '$Algorithm'. Allowed: $($ValidAlgorithms -join ', ')."
}

$allRuns = @(
    @{
        SceneKey = "rts"
        PresetId = "nav_bake_showcase_raylib"
        ArtifactRoot = Join-Path $repoRoot "artifacts\acceptance\nav-bake-showcase"
        ConfigPath = Join-Path $repoRoot "mods\showcases\nav_bake\DynamicNavBakeShowcaseMod\assets\Showcases\DynamicNavBake\nav_bake_dynamic_rts.json"
    },
    @{
        SceneKey = "open_world"
        PresetId = "nav_bake_open_world_64x64_raylib"
        ArtifactRoot = Join-Path $repoRoot "artifacts\acceptance\nav-bake-open-world-64x64"
        ConfigPath = Join-Path $repoRoot "mods\showcases\nav_bake\DynamicNavBakeShowcaseMod\assets\Showcases\DynamicNavBake\nav_bake_open_world_64x64.json"
    }
)

$runs = if ([string]::IsNullOrWhiteSpace($Scene)) {
    $allRuns
}
else {
    @($allRuns | Where-Object { $_.SceneKey -eq $Scene })
}

if ($runs.Count -le 0) {
    throw "No Dynamic NavBake scenes selected after validating -Scene='$Scene'."
}

$requiredProjects = Get-RequiredReleaseProjects -SceneKeys @($runs | ForEach-Object { $_.SceneKey })
Ensure-ReleaseBuild -Mode $Build -Projects $requiredProjects

$algorithms = if ([string]::IsNullOrWhiteSpace($Algorithm)) {
    $ValidAlgorithms
}
else {
    @($Algorithm)
}

try {
    foreach ($run in $runs) {
        $timeline = Get-RaylibAutoTimeline -ConfigPath $run.ConfigPath
        New-Item -ItemType Directory -Force -Path $run.ArtifactRoot | Out-Null
        foreach ($algo in $algorithms) {
            Invoke-DynamicNavBakeRaylibRun `
                -PresetId $run.PresetId `
                -Algorithm $algo `
                -ArtifactRoot $run.ArtifactRoot `
                -Timeline $timeline
        }
    }
}
finally {
    Clear-DynamicNavBakeAutoEnv
}

$matrixLabel = if ([string]::IsNullOrWhiteSpace($Scene)) { "2 scenes x 3 algorithms" } else { "scene=$Scene algorithm=$Algorithm" }
Write-Host "Dynamic NavBake Raylib acceptance completed for $matrixLabel."
Write-Host "Artifacts retained under artifacts/acceptance/nav-bake-showcase and/or artifacts/acceptance/nav-bake-open-world-64x64."
