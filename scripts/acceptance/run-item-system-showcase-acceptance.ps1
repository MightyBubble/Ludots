param(
    [string]$ScreenshotPath = "D:\001_AI\LudotsDev\Ludots-item-worktree\artifacts\acceptance\item-system-showcase\item-system-showcase-raylib.png",
    [string]$DiagnosticPath = "D:\001_AI\LudotsDev\Ludots-item-worktree\artifacts\acceptance\item-system-showcase\item-system-showcase-raylib-diagnostic.log",
    [int]$ScreenshotFrame = 180,
    [int]$KillAfterSeconds = 25,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = "D:\001_AI\LudotsDev\Ludots-item-worktree"
$raylibScript = Join-Path $repoRoot "scripts\acceptance\run-item-system-showcase-raylib.ps1"

New-Item -ItemType Directory -Path (Join-Path $repoRoot "artifacts\acceptance\item-system-showcase") -Force | Out-Null
$notBeforeUtc = [DateTimeOffset]::UtcNow.ToString("O")

& powershell -NoProfile -ExecutionPolicy Bypass -File $raylibScript `
    -ScreenshotPath $ScreenshotPath `
    -DiagnosticPath $DiagnosticPath `
    -ScreenshotFrame $ScreenshotFrame `
    -KillAfterSeconds $KillAfterSeconds

$env:LUDOTS_ACCEPTANCE_REQUIRE_RAYLIB_EVIDENCE = "1"
$env:LUDOTS_ACCEPTANCE_SCREENSHOT_PATH = $ScreenshotPath
$env:LUDOTS_ACCEPTANCE_DIAGNOSTIC_PATH = $DiagnosticPath
$env:LUDOTS_ACCEPTANCE_SCREENSHOT_NOT_BEFORE_UTC = $notBeforeUtc

Push-Location $repoRoot
try {
    dotnet test src\Tests\GasTests\GasTests.csproj -c $Configuration --filter ItemSystemShowcase
}
finally {
    Pop-Location
    Remove-Item Env:LUDOTS_ACCEPTANCE_REQUIRE_RAYLIB_EVIDENCE -ErrorAction SilentlyContinue
    Remove-Item Env:LUDOTS_ACCEPTANCE_SCREENSHOT_PATH -ErrorAction SilentlyContinue
    Remove-Item Env:LUDOTS_ACCEPTANCE_DIAGNOSTIC_PATH -ErrorAction SilentlyContinue
    Remove-Item Env:LUDOTS_ACCEPTANCE_SCREENSHOT_NOT_BEFORE_UTC -ErrorAction SilentlyContinue
}
