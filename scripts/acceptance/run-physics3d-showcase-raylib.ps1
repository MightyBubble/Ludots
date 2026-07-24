param(
    [string]$ScreenshotPath = "",
    [int]$ScreenshotFrame = 120,
    [string]$DiagnosticPath = "",
    [int]$KillAfterSeconds = 60
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$artifactRoot = Join-Path $repoRoot "artifacts\acceptance\physics3d-showcase"
$captureRoot = Join-Path $artifactRoot "capture-runtime"
$launcher = Join-Path $repoRoot "src\Tools\Ludots.Launcher.Cli\bin\Release\net8.0\Ludots.Launcher.Cli.exe"

if ([string]::IsNullOrWhiteSpace($ScreenshotPath)) {
    $ScreenshotPath = Join-Path $artifactRoot "screens\000_start.png"
}
if ([string]::IsNullOrWhiteSpace($DiagnosticPath)) {
    $DiagnosticPath = Join-Path $artifactRoot "raylib-diagnostic.log"
}
if ($ScreenshotFrame -le 0) {
    throw "ScreenshotFrame must be positive."
}
if ($KillAfterSeconds -le 0) {
    throw "KillAfterSeconds must be positive."
}
if (-not (Test-Path -LiteralPath $launcher)) {
    throw "Release launcher is missing: $launcher"
}

$screenshotFullPath = [System.IO.Path]::GetFullPath($ScreenshotPath)
$diagnosticFullPath = [System.IO.Path]::GetFullPath($DiagnosticPath)
$stdoutPath = Join-Path $captureRoot "launcher.stdout.log"
$stderrPath = Join-Path $captureRoot "launcher.stderr.log"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $screenshotFullPath) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $diagnosticFullPath) | Out-Null
New-Item -ItemType Directory -Force -Path $captureRoot | Out-Null

$previousScreenshotWriteUtc = if (Test-Path -LiteralPath $screenshotFullPath) {
    (Get-Item -LiteralPath $screenshotFullPath).LastWriteTimeUtc
}
else {
    [DateTime]::MinValue
}
$previousDiagnosticWriteUtc = if (Test-Path -LiteralPath $diagnosticFullPath) {
    (Get-Item -LiteralPath $diagnosticFullPath).LastWriteTimeUtc
}
else {
    [DateTime]::MinValue
}

$startedAt = Get-Date
$launcherProcess = $null
$completed = $false
$env:LUDOTS_TAKE_SCREENSHOT_PATH = $screenshotFullPath
$env:LUDOTS_TAKE_SCREENSHOT_FRAME = $ScreenshotFrame.ToString()
$env:LUDOTS_AUTO_EXIT_FRAME = ($ScreenshotFrame + 10).ToString()
$env:LUDOTS_RAYLIB_DIAGNOSTIC_PATH = $diagnosticFullPath
$env:LUDOTS_RAYLIB_TIMING_LOG_INTERVAL_FRAMES = "30"

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
        $screenshotReady = (Test-Path -LiteralPath $screenshotFullPath) -and
            (Get-Item -LiteralPath $screenshotFullPath).LastWriteTimeUtc -gt $previousScreenshotWriteUtc
        $diagnosticReady = (Test-Path -LiteralPath $diagnosticFullPath) -and
            (Get-Item -LiteralPath $diagnosticFullPath).LastWriteTimeUtc -gt $previousDiagnosticWriteUtc
        $autoExitRecorded = $diagnosticReady -and
            (Select-String -LiteralPath $diagnosticFullPath -Pattern "auto-exit frame=" -Quiet)
        if ($screenshotReady -and $autoExitRecorded) {
            $completed = $true
            break
        }

        Start-Sleep -Milliseconds 250
    }

    if (-not $completed) {
        throw "Timed out waiting for fresh Physics3D Raylib screenshot and auto-exit evidence."
    }

    for ($attempt = 0; $attempt -lt 20 -and -not $launcherProcess.HasExited; $attempt++) {
        Start-Sleep -Milliseconds 100
    }
}
finally {
    if ($null -ne $launcherProcess -and -not $launcherProcess.HasExited) {
        Stop-Process -Id $launcherProcess.Id -Force -ErrorAction SilentlyContinue
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
    Remove-Item Env:LUDOTS_TAKE_SCREENSHOT_FRAME -ErrorAction SilentlyContinue
    Remove-Item Env:LUDOTS_AUTO_EXIT_FRAME -ErrorAction SilentlyContinue
    Remove-Item Env:LUDOTS_RAYLIB_DIAGNOSTIC_PATH -ErrorAction SilentlyContinue
    Remove-Item Env:LUDOTS_RAYLIB_TIMING_LOG_INTERVAL_FRAMES -ErrorAction SilentlyContinue
}

$screenshot = Get-Item -LiteralPath $screenshotFullPath
if ($screenshot.Length -lt 10KB) {
    throw "Physics3D Raylib screenshot is unexpectedly small: $($screenshot.Length) bytes."
}

$stdout = if (Test-Path -LiteralPath $stdoutPath) { Get-Content -Raw -Encoding UTF8 $stdoutPath } else { "" }
$stderr = if (Test-Path -LiteralPath $stderrPath) { Get-Content -Raw -Encoding UTF8 $stderrPath } else { "" }
$diagnostic = Get-Content -Raw -Encoding UTF8 $diagnosticFullPath
$combinedLog = $stdout + [Environment]::NewLine + $stderr + [Environment]::NewLine + $diagnostic
if ($combinedLog -match "(?m)\[ERR\]") {
    throw "Physics3D Raylib evidence contains an [ERR] log entry."
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

Write-Output "[OK] Physics3D Raylib screenshot: $screenshotFullPath"
Write-Output "[OK] Primitive instances: $maximumPrimitiveInstances"
Write-Output "[OK] UI render: $($maximumUiRenderMilliseconds.ToString('0.###', [Globalization.CultureInfo]::InvariantCulture)) ms"
Write-Output "[OK] No [ERR] entries: $diagnosticFullPath"
