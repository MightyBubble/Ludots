param(
    [string]$ScreenshotPath,
    [int]$ScreenshotFrame = 120,
    [string]$DiagnosticPath = "",
    [int]$KillAfterSeconds = 12,
    [string]$StartupMapId = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = "D:\001_AI\LudotsDev\Ludots-item-worktree"
$launcher = Join-Path $repoRoot "src\Tools\Ludots.Launcher.Cli\bin\Release\net8.0\Ludots.Launcher.Cli.exe"
$captureRoot = Join-Path $repoRoot "artifacts\acceptance\item-system-showcase\capture-runtime"
$overrideModRoot = Join-Path $captureRoot "startup-map-override"

if ([string]::IsNullOrWhiteSpace($ScreenshotPath)) {
    throw "ScreenshotPath is required."
}

$env:LUDOTS_TAKE_SCREENSHOT_PATH = $ScreenshotPath
$env:LUDOTS_TAKE_SCREENSHOT_FRAME = $ScreenshotFrame.ToString()

if ([string]::IsNullOrWhiteSpace($DiagnosticPath)) {
    Remove-Item Env:LUDOTS_RAYLIB_DIAGNOSTIC_PATH -ErrorAction SilentlyContinue
}
else {
    $env:LUDOTS_RAYLIB_DIAGNOSTIC_PATH = $DiagnosticPath
}

New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($ScreenshotPath)) -Force | Out-Null
if (-not [string]::IsNullOrWhiteSpace($DiagnosticPath)) {
    New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($DiagnosticPath)) -Force | Out-Null
}

$selectors = New-Object System.Collections.Generic.List[string]
$selectors.Add("mod:ItemSystemShowcaseMod")

if (-not [string]::IsNullOrWhiteSpace($StartupMapId)) {
    if (Test-Path $overrideModRoot) {
        Remove-Item $overrideModRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Path (Join-Path $overrideModRoot "assets") -Force | Out-Null

    @"
{
  "name": "ItemSystemShowcaseCaptureOverride",
  "version": "1.0.0",
  "description": "Resource-only startup map override used by acceptance capture scripts.",
  "main": "",
  "priority": 1000,
  "dependencies": {
    "ItemSystemShowcaseMod": "*"
  },
  "tags": ["acceptance", "capture", "item-showcase"]
}
"@ | Set-Content (Join-Path $overrideModRoot "mod.json") -Encoding utf8

    @"
{
  "startupMapId": "$StartupMapId"
}
"@ | Set-Content (Join-Path $overrideModRoot "assets\game.json") -Encoding utf8

    $selectors.Add("path:$overrideModRoot")
}

$previousScreenshotWriteUtc = if (Test-Path $ScreenshotPath) { (Get-Item $ScreenshotPath).LastWriteTimeUtc } else { [DateTime]::MinValue }
$previousDiagnosticWriteUtc = if (-not [string]::IsNullOrWhiteSpace($DiagnosticPath) -and (Test-Path $DiagnosticPath)) {
    (Get-Item $DiagnosticPath).LastWriteTimeUtc
}
else {
    [DateTime]::MinValue
}

$startedAt = Get-Date
$launchArgs = @("launch") + $selectors + @("--adapter", "raylib", "--build", "never")
$launcherProcess = Start-Process -FilePath $launcher `
    -ArgumentList $launchArgs `
    -WorkingDirectory $repoRoot `
    -PassThru

$deadline = [DateTime]::UtcNow.AddSeconds($KillAfterSeconds)
$screenshotReady = $false
$diagnosticReady = [string]::IsNullOrWhiteSpace($DiagnosticPath)

try {
    while ([DateTime]::UtcNow -lt $deadline) {
        if (-not $screenshotReady -and (Test-Path $ScreenshotPath)) {
            $screenshotReady = (Get-Item $ScreenshotPath).LastWriteTimeUtc -gt $previousScreenshotWriteUtc
        }

        if (-not $diagnosticReady -and -not [string]::IsNullOrWhiteSpace($DiagnosticPath) -and (Test-Path $DiagnosticPath)) {
            $diagnosticReady = (Get-Item $DiagnosticPath).LastWriteTimeUtc -gt $previousDiagnosticWriteUtc
        }

        if ($screenshotReady -and $diagnosticReady) {
            break
        }

        Start-Sleep -Milliseconds 500
    }

    if (-not $screenshotReady -or -not $diagnosticReady) {
        throw "Timed out waiting for fresh Raylib evidence files."
    }
}
finally {
    if (-not $launcherProcess.HasExited) {
        & taskkill /PID $launcherProcess.Id /T /F 2>$null | Out-Null
    }

    Get-Process dotnet -ErrorAction SilentlyContinue |
        Where-Object { $_.StartTime -ge $startedAt.AddSeconds(-1) -and $_.MainWindowTitle -eq "Ludots Engine" } |
        ForEach-Object { & taskkill /PID $_.Id /T /F 2>$null | Out-Null }

    if (Test-Path $overrideModRoot) {
        Remove-Item $overrideModRoot -Recurse -Force
    }
}
