param(
    [string]$ScreenshotPath = "",
    [string]$PlaybackPath = "",
    [string]$DiagnosticPath = "",
    [int]$KillAfterSeconds = 300
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$artifactRoot = Join-Path $repoRoot "artifacts\acceptance\physics3d-showcase"
$captureRoot = Join-Path $artifactRoot "capture-runtime"
$launcher = Join-Path $repoRoot "src\Tools\Ludots.Launcher.Cli\bin\Release\net8.0\Ludots.Launcher.Cli.exe"

if ([string]::IsNullOrWhiteSpace($ScreenshotPath)) {
    $ScreenshotPath = Join-Path $artifactRoot "screens\tour.png"
}
if ([string]::IsNullOrWhiteSpace($PlaybackPath)) {
    $PlaybackPath = Join-Path $PSScriptRoot "physics3d-showcase-tour.playback.json"
}
if ([string]::IsNullOrWhiteSpace($DiagnosticPath)) {
    $DiagnosticPath = Join-Path $artifactRoot "raylib-diagnostic.log"
}
if ($KillAfterSeconds -le 0) {
    throw "KillAfterSeconds must be positive."
}
if (-not (Test-Path -LiteralPath $launcher)) {
    throw "Release launcher is missing: $launcher"
}
if (-not (Test-Path -LiteralPath $PlaybackPath)) {
    throw "Physics3D Raylib input playback is missing: $PlaybackPath"
}

$playbackFullPath = [System.IO.Path]::GetFullPath($PlaybackPath)
$playback = Get-Content -Raw -Encoding UTF8 $playbackFullPath | ConvertFrom-Json
if ($playback.version -ne 1 -or $null -eq $playback.events -or $playback.events.Count -eq 0) {
    throw "Physics3D Raylib input playback must contain version 1 and at least one event."
}

$expectedCaptureLabels = @(
    "capture:scanner-capsule-sweep",
    "capture:material-friction",
    "capture:platform-route-complete",
    "capture:wind-vortex-reverse",
    "capture:traversal-route-complete",
    "capture:wheel-physical",
    "capture:wheel-box",
    "capture:wheel-scanning",
    "capture:ragdoll-impact",
    "capture:ragdoll-recovery-blocked",
    "capture:ragdoll-recovered",
    "capture:constraint-reverse-paused",
    "capture:rebuild-difference",
    "capture:rebuild-pass",
    "capture:scale-city-50k")
$captureMarkers = @($playback.events | Where-Object {
    $_.kind -eq "Marker" -and $_.label -is [string] -and $_.label.StartsWith("capture:", [StringComparison]::Ordinal)
})
if ($captureMarkers.Count -ne $expectedCaptureLabels.Count) {
    throw "Physics3D Raylib tour capture count mismatch: expected=$($expectedCaptureLabels.Count), actual=$($captureMarkers.Count)."
}
for ($index = 0; $index -lt $expectedCaptureLabels.Count; $index++) {
    if ($captureMarkers[$index].label -cne $expectedCaptureLabels[$index]) {
        throw "Physics3D Raylib capture $index must be '$($expectedCaptureLabels[$index])', actual='$($captureMarkers[$index].label)'."
    }
}

$captureFrames = @($captureMarkers | ForEach-Object { [int]$_.frame + 1 })
for ($index = 1; $index -lt $captureFrames.Count; $index++) {
    if ($captureFrames[$index] -le $captureFrames[$index - 1]) {
        throw "Physics3D capture marker frames must increase strictly."
    }
}

function Find-RequiredPlaybackEvent(
    [object[]]$events,
    [string]$kind,
    [string]$property,
    [string]$value,
    [int]$afterIndex) {
    for ($index = $afterIndex + 1; $index -lt $events.Count; $index++) {
        $event = $events[$index]
        $candidate = $event.PSObject.Properties[$property]
        if ($event.kind -ceq $kind -and $null -ne $candidate -and $candidate.Value -ceq $value) {
            return $index
        }
    }

    throw "Physics3D Raylib tour is missing $kind $property='$value' after event $afterIndex."
}

$platformSceneIndex = Find-RequiredPlaybackEvent $playback.events "UiClick" "elementId" "physics3d-scene-platformstation" -1
$platformGuideIndex = Find-RequiredPlaybackEvent $playback.events "UiClick" "elementId" "physics3d-route-guide" $platformSceneIndex
$null = Find-RequiredPlaybackEvent $playback.events "Marker" "label" "capture:platform-route-complete" $platformGuideIndex
$traversalSceneIndex = Find-RequiredPlaybackEvent $playback.events "UiClick" "elementId" "physics3d-scene-traversalcourse" $platformGuideIndex
$traversalGuideIndex = Find-RequiredPlaybackEvent $playback.events "UiClick" "elementId" "physics3d-route-guide" $traversalSceneIndex
$null = Find-RequiredPlaybackEvent $playback.events "Marker" "label" "capture:traversal-route-complete" $traversalGuideIndex

$scaleSceneIndex = Find-RequiredPlaybackEvent $playback.events "UiClick" "elementId" "physics3d-scene-scalecity" $traversalGuideIndex
$scalePresetIndex = Find-RequiredPlaybackEvent $playback.events "UiClick" "elementId" "physics3d-benchmark-50000" $scaleSceneIndex
$scalePulseIndex = Find-RequiredPlaybackEvent $playback.events "UiClick" "elementId" "physics3d-action-impact" $scalePresetIndex
$scaleCaptureIndex = Find-RequiredPlaybackEvent $playback.events "Marker" "label" "capture:scale-city-50k" $scalePulseIndex
for ($index = $scaleSceneIndex + 1; $index -le $scaleCaptureIndex; $index++) {
    $event = $playback.events[$index]
    if ($event.kind -ceq "UiClick" -and $event.elementId -ceq "physics3d-action-pause") {
        throw "Physics3D Scale City tour must remain running from scene selection through its 50K capture."
    }
}
$scaleConfigPath = Join-Path $repoRoot "mods\showcases\capability_standard\CapabilityStandardPhysics3DShowcaseMod\assets\CapabilityStandardPhysics3DShowcaseConfig.json"
$scaleConfig = Get-Content -Raw -Encoding UTF8 $scaleConfigPath | ConvertFrom-Json
$materialSceneIndex = Find-RequiredPlaybackEvent $playback.events "UiClick" "elementId" "physics3d-scene-materialhill" -1
$materialLaunchIndex = Find-RequiredPlaybackEvent $playback.events "UiClick" "elementId" "physics3d-action-impact" $materialSceneIndex
$materialCaptureIndex = Find-RequiredPlaybackEvent $playback.events "Marker" "label" "capture:material-friction" $materialLaunchIndex
$materialCompletionTicks = [int]$scaleConfig.materialHill.completionTimeLimitTicks
if ($materialCompletionTicks -le 0) {
    throw "Physics3D Material Hill completionTimeLimitTicks must be positive."
}
$materialLaunchFrame = [int]$playback.events[$materialLaunchIndex].frame
$materialCaptureFrame = [int]$playback.events[$materialCaptureIndex].frame
if ($materialCaptureFrame - $materialLaunchFrame -lt $materialCompletionTicks) {
    throw "Physics3D Material Hill tour must leave at least $materialCompletionTicks render frames for the authored fixed-step completion window."
}
$performanceWindowSamples = [int]$scaleConfig.scaleCity.performanceWindowSampleCount
if ($performanceWindowSamples -le 0) {
    throw "Physics3D Scale City performanceWindowSampleCount must be positive."
}

$minimumPerformanceWindowRenderFrames = [int]($performanceWindowSamples * 4)
$scalePresetFrame = [int]$playback.events[$scalePresetIndex].frame
$scalePulseFrame = [int]$playback.events[$scalePulseIndex].frame
$scaleCaptureFrame = [int]$playback.events[$scaleCaptureIndex].frame
if ($scalePulseFrame - $scalePresetFrame -lt $minimumPerformanceWindowRenderFrames) {
    throw "Physics3D Scale City tour must leave at least $minimumPerformanceWindowRenderFrames render frames after 50K selection for the configured $performanceWindowSamples-sample physics/full-frame windows."
}
if ($scaleCaptureFrame -le $scalePulseFrame) {
    throw "Physics3D Scale City capture must occur after City Pulse can execute on an authoritative step."
}

$lastEventFrame = [int](($playback.events | Measure-Object -Property frame -Maximum).Maximum)
$autoExitFrame = [Math]::Max($lastEventFrame + 15, $captureFrames[-1] + 10)
$screenshotFullPath = [System.IO.Path]::GetFullPath($ScreenshotPath)
$diagnosticFullPath = [System.IO.Path]::GetFullPath($DiagnosticPath)
$stdoutPath = Join-Path $captureRoot "launcher.stdout.log"
$stderrPath = Join-Path $captureRoot "launcher.stderr.log"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $screenshotFullPath) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $diagnosticFullPath) | Out-Null
New-Item -ItemType Directory -Force -Path $captureRoot | Out-Null

function Get-SequencedScreenshotPath(
    [string]$targetPath,
    [int]$sequenceIndex,
    [int]$frame) {
    $directory = [System.IO.Path]::GetDirectoryName($targetPath)
    $extension = [System.IO.Path]::GetExtension($targetPath)
    if ([string]::IsNullOrWhiteSpace($extension)) {
        $extension = ".png"
    }

    $fileName = [System.IO.Path]::GetFileNameWithoutExtension($targetPath)
    if ([string]::IsNullOrWhiteSpace($fileName)) {
        $fileName = "screenshot"
    }

    $sequencedName = "{0}_{1:000}_f{2:0000}{3}" -f $fileName, ($sequenceIndex + 1), $frame, $extension
    return Join-Path $directory $sequencedName
}

$expectedScreenshots = @()
$previousScreenshotWriteUtc = @()
for ($index = 0; $index -lt $captureFrames.Count; $index++) {
    $path = Get-SequencedScreenshotPath `
        $screenshotFullPath `
        $index `
        $captureFrames[$index]
    $expectedScreenshots += $path
    $previousScreenshotWriteUtc += if (Test-Path -LiteralPath $path) {
        (Get-Item -LiteralPath $path).LastWriteTimeUtc
    }
    else {
        [DateTime]::MinValue
    }
}

if (Test-Path -LiteralPath $diagnosticFullPath) {
    Remove-Item -LiteralPath $diagnosticFullPath -Force
}
$previousDiagnosticWriteUtc = [DateTime]::MinValue

$startedAt = Get-Date
$launcherProcess = $null
$gameProcessId = $null
$completed = $false
$env:LUDOTS_TAKE_SCREENSHOT_PATH = $screenshotFullPath
$env:LUDOTS_TAKE_SCREENSHOT_FRAMES = [string]::Join(",", $captureFrames)
$env:LUDOTS_AUTO_EXIT_FRAME = $autoExitFrame.ToString()
$env:LUDOTS_RAYLIB_INPUT_PLAYBACK_PATH = $playbackFullPath
$env:LUDOTS_RAYLIB_DIAGNOSTIC_PATH = $diagnosticFullPath
$env:LUDOTS_RAYLIB_TIMING_LOG_INTERVAL_FRAMES = "60"

try {
    $launcherProcess = Start-Process -FilePath $launcher `
        -ArgumentList @(
            "launch",
            "preset:capability_standard_physics3d_showcase_raylib",
            "--adapter",
            "raylib",
            "--build",
            "never") `
        -WorkingDirectory $repoRoot `
        -WindowStyle Hidden `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -PassThru

    $deadline = [DateTime]::UtcNow.AddSeconds($KillAfterSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($null -eq $gameProcessId -and (Test-Path -LiteralPath $stdoutPath)) {
            $launchOutput = Get-Content -Raw -Encoding UTF8 $stdoutPath
            if (-not [string]::IsNullOrEmpty($launchOutput)) {
                $pidMatch = [regex]::Match($launchOutput, "(?m)^pid=(\d+)\s*$")
                if ($pidMatch.Success) {
                    $gameProcessId = [int]$pidMatch.Groups[1].Value
                }
            }
        }

        $screenshotsReady = $true
        for ($index = 0; $index -lt $expectedScreenshots.Count; $index++) {
            $path = $expectedScreenshots[$index]
            if (-not (Test-Path -LiteralPath $path) -or
                (Get-Item -LiteralPath $path).LastWriteTimeUtc -le $previousScreenshotWriteUtc[$index]) {
                $screenshotsReady = $false
                break
            }
        }

        $diagnosticReady = (Test-Path -LiteralPath $diagnosticFullPath) -and
            (Get-Item -LiteralPath $diagnosticFullPath).LastWriteTimeUtc -gt $previousDiagnosticWriteUtc
        $autoExitRecorded = $diagnosticReady -and
            (Select-String -LiteralPath $diagnosticFullPath -Pattern "auto-exit frame=$autoExitFrame" -Quiet)
        if ($screenshotsReady -and $autoExitRecorded) {
            $completed = $true
            break
        }

        if ($null -ne $gameProcessId -and
            $null -eq (Get-Process -Id $gameProcessId -ErrorAction SilentlyContinue)) {
            $earlyStdout = if (Test-Path -LiteralPath $stdoutPath) {
                Get-Content -Raw -Encoding UTF8 $stdoutPath
            }
            else {
                ""
            }
            $earlyStderr = if (Test-Path -LiteralPath $stderrPath) {
                Get-Content -Raw -Encoding UTF8 $stderrPath
            }
            else {
                ""
            }
            throw "Physics3D Raylib player tour exited before all evidence was captured.`n$earlyStdout`n$earlyStderr"
        }

        Start-Sleep -Milliseconds 250
    }

    if (-not $completed) {
        throw "Timed out waiting for the complete Physics3D Raylib player tour."
    }

    for ($attempt = 0; $attempt -lt 30 -and -not $launcherProcess.HasExited; $attempt++) {
        Start-Sleep -Milliseconds 100
    }
}
finally {
    if ($null -ne $launcherProcess -and -not $launcherProcess.HasExited) {
        Stop-Process -Id $launcherProcess.Id -Force -ErrorAction SilentlyContinue
    }

    if ($null -ne $gameProcessId -and
        $null -ne (Get-Process -Id $gameProcessId -ErrorAction SilentlyContinue)) {
        Stop-Process -Id $gameProcessId -Force -ErrorAction SilentlyContinue
    }

    if (-not $completed) {
        Get-Process dotnet -ErrorAction SilentlyContinue |
            Where-Object {
                $_.StartTime -ge $startedAt.AddSeconds(-1) -and
                $_.MainWindowTitle -eq "Ludots Engine - Physics3D Playground"
            } |
            ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
    }

    Remove-Item Env:LUDOTS_TAKE_SCREENSHOT_PATH -ErrorAction SilentlyContinue
    Remove-Item Env:LUDOTS_TAKE_SCREENSHOT_FRAMES -ErrorAction SilentlyContinue
    Remove-Item Env:LUDOTS_AUTO_EXIT_FRAME -ErrorAction SilentlyContinue
    Remove-Item Env:LUDOTS_RAYLIB_INPUT_PLAYBACK_PATH -ErrorAction SilentlyContinue
    Remove-Item Env:LUDOTS_RAYLIB_DIAGNOSTIC_PATH -ErrorAction SilentlyContinue
    Remove-Item Env:LUDOTS_RAYLIB_TIMING_LOG_INTERVAL_FRAMES -ErrorAction SilentlyContinue
}

for ($index = 0; $index -lt $expectedScreenshots.Count; $index++) {
    $screenshot = Get-Item -LiteralPath $expectedScreenshots[$index]
    if ($screenshot.Length -lt 10KB) {
        throw "Physics3D Raylib screenshot is unexpectedly small: $($screenshot.FullName), $($screenshot.Length) bytes."
    }
}

$stdout = if (Test-Path -LiteralPath $stdoutPath) { Get-Content -Raw -Encoding UTF8 $stdoutPath } else { "" }
$stderr = if (Test-Path -LiteralPath $stderrPath) { Get-Content -Raw -Encoding UTF8 $stderrPath } else { "" }
$diagnostic = Get-Content -Raw -Encoding UTF8 $diagnosticFullPath
$combinedLog = $stdout + [Environment]::NewLine + $stderr + [Environment]::NewLine + $diagnostic
if ($combinedLog -match "(?m)\[ERR\]") {
    throw "Physics3D Raylib evidence contains an [ERR] log entry."
}

$playbackEventCount = [regex]::Matches($diagnostic, "input-playback event=").Count
if ($playbackEventCount -ne $playback.events.Count) {
    throw "Physics3D Raylib playback did not execute every event: expected=$($playback.events.Count), actual=$playbackEventCount."
}
foreach ($capture in $captureMarkers) {
    if ($diagnostic -notmatch [regex]::Escape("target=$($capture.label)")) {
        throw "Physics3D Raylib playback did not reach capture marker '$($capture.label)'."
    }
}

$primitiveMatches = [regex]::Matches($diagnostic, "primInstances=(\d+)")
$maximumPrimitiveInstances = 0
foreach ($match in $primitiveMatches) {
    $maximumPrimitiveInstances = [Math]::Max($maximumPrimitiveInstances, [int]$match.Groups[1].Value)
}
if ($maximumPrimitiveInstances -le 0) {
    throw "Physics3D Raylib evidence did not draw any primitive instances."
}

$uiMatches = [regex]::Matches($diagnostic, "uiRender=([0-9]+(?:\.[0-9]+)?)")
$maximumUiRenderMilliseconds = 0.0
foreach ($match in $uiMatches) {
    $value = [double]::Parse($match.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture)
    $maximumUiRenderMilliseconds = [Math]::Max($maximumUiRenderMilliseconds, $value)
}
if ($maximumUiRenderMilliseconds -le 0.0) {
    throw "Physics3D Raylib evidence did not render the player lab UI."
}

for ($index = 0; $index -lt $captureMarkers.Count; $index++) {
    Write-Output "[OK] $($captureMarkers[$index].label): $($expectedScreenshots[$index])"
}
Write-Output "[OK] Playback events: $playbackEventCount"
Write-Output "[OK] Primitive instances: $maximumPrimitiveInstances"
Write-Output "[OK] UI render: $($maximumUiRenderMilliseconds.ToString('0.###', [Globalization.CultureInfo]::InvariantCulture)) ms"
Write-Output "[OK] No [ERR] entries: $diagnosticFullPath"
