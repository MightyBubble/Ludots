param(
    [string]$ScreenshotPath,
    [int]$ScreenshotFrame = 120,
    [string]$DiagnosticPath = "",
    [int]$KillAfterSeconds = 12
)

$ErrorActionPreference = "Stop"

$repoRoot = "D:\001_AI\LudotsDev\Ludots-item-worktree"
$launcher = Join-Path $repoRoot "src\Tools\Ludots.Launcher.Cli\bin\Release\net8.0\Ludots.Launcher.Cli.exe"

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

$previousScreenshotWriteUtc = if (Test-Path $ScreenshotPath) { (Get-Item $ScreenshotPath).LastWriteTimeUtc } else { [DateTime]::MinValue }
$previousDiagnosticWriteUtc = if (-not [string]::IsNullOrWhiteSpace($DiagnosticPath) -and (Test-Path $DiagnosticPath)) {
    (Get-Item $DiagnosticPath).LastWriteTimeUtc
}
else {
    [DateTime]::MinValue
}

$startedAt = Get-Date
$launcherProcess = Start-Process -FilePath $launcher `
    -ArgumentList @("launch", "mod:ItemSystemShowcaseMod", "--adapter", "raylib", "--build", "never") `
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
}
