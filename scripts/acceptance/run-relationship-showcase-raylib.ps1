param(
    [string]$ScreenshotPath,
    [int]$ScreenshotFrame = 120,
    [string]$DiagnosticPath = "",
    [int]$KillAfterSeconds = 20
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$launcher = Join-Path $repoRoot "scripts\run-mod-launcher.cmd"

if ([string]::IsNullOrWhiteSpace($ScreenshotPath)) {
    throw "ScreenshotPath is required."
}

$screenshotFullPath = [System.IO.Path]::GetFullPath($ScreenshotPath)
$screenshotDir = Split-Path -Parent $screenshotFullPath
if (-not [string]::IsNullOrWhiteSpace($screenshotDir)) {
    New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
}

$env:LUDOTS_TAKE_SCREENSHOT_PATH = $screenshotFullPath
$env:LUDOTS_TAKE_SCREENSHOT_FRAME = $ScreenshotFrame.ToString()

if ([string]::IsNullOrWhiteSpace($DiagnosticPath)) {
    Remove-Item Env:LUDOTS_RAYLIB_DIAGNOSTIC_PATH -ErrorAction SilentlyContinue
}
else {
    $diagnosticFullPath = [System.IO.Path]::GetFullPath($DiagnosticPath)
    $diagnosticDir = Split-Path -Parent $diagnosticFullPath
    if (-not [string]::IsNullOrWhiteSpace($diagnosticDir)) {
        New-Item -ItemType Directory -Force -Path $diagnosticDir | Out-Null
    }

    $env:LUDOTS_RAYLIB_DIAGNOSTIC_PATH = $diagnosticFullPath
}

try {
    Push-Location $repoRoot
    try {
        $launchOutput = & $launcher cli launch mod:RelationshipShowcaseMod --adapter raylib --build never 2>&1
        $launchOutput | ForEach-Object { $_ }
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }

        $pidLine = $launchOutput |
            Where-Object { $_ -is [string] -and $_ -match '^pid=(\d+)$' } |
            Select-Object -First 1
        if ($null -eq $pidLine) {
            throw "Launcher did not report a child pid."
        }

        $targetPid = [int]($pidLine -replace '^pid=', '')
        Start-Sleep -Seconds $KillAfterSeconds

        $targetProcess = Get-Process -Id $targetPid -ErrorAction SilentlyContinue
        if ($null -ne $targetProcess) {
            Stop-Process -Id $targetPid -Force
        }
    }
    finally {
        Pop-Location
    }
}
