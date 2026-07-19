param(
    [string]$ScreenshotPath,
    [int]$ScreenshotFrame = 120,
    [string]$DiagnosticPath = "",
    [int]$KillAfterSeconds = 12
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
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

$startedAt = Get-Date
$killer = Start-Job -ScriptBlock {
    param([DateTime]$startedAt, [int]$killAfterSeconds)
    Start-Sleep -Seconds $killAfterSeconds
    Get-Process dotnet -ErrorAction SilentlyContinue |
        Where-Object { $_.StartTime -ge $startedAt.AddSeconds(-2) } |
        Stop-Process -Force
} -ArgumentList $startedAt, $KillAfterSeconds

try {
    & $launcher launch mod:UxPrototypeMod --adapter raylib --build never
}
finally {
    Wait-Job $killer -Timeout ($KillAfterSeconds + 5) | Out-Null
    Receive-Job $killer -ErrorAction SilentlyContinue | Out-Null
    Remove-Job $killer -Force -ErrorAction SilentlyContinue
}
