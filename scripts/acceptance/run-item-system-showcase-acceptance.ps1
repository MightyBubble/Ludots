param(
    [string]$ScreenshotPath = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path "artifacts\acceptance\item-system-showcase\item-system-showcase-raylib.png"),
    [string]$DiagnosticPath = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path "artifacts\acceptance\item-system-showcase\item-system-showcase-raylib-diagnostic.log"),
    [int]$ScreenshotFrame = 180,
    [int]$KillAfterSeconds = 45,
    [string]$Configuration = "Release",
    [int]$CaptureRoomScreenshots = 1,
    [string]$RootModName = "ItemSystemShowcaseMod",
    [string]$TestFilter = "ItemSystemShowcase",
    [string]$StartupMapId = "item_system_showcase_forge_socket_lab"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$raylibScript = Join-Path $repoRoot "scripts\acceptance\run-item-system-showcase-raylib.ps1"
$roomCaptureScript = Join-Path $repoRoot "scripts\acceptance\capture-item-system-showcase-rooms.ps1"

New-Item -ItemType Directory -Path (Join-Path $repoRoot "artifacts\acceptance\item-system-showcase") -Force | Out-Null
$notBeforeUtc = [DateTimeOffset]::UtcNow.ToString("O")

& powershell -NoProfile -ExecutionPolicy Bypass -File $raylibScript `
    -ScreenshotPath $ScreenshotPath `
    -DiagnosticPath $DiagnosticPath `
    -ScreenshotFrame $ScreenshotFrame `
    -KillAfterSeconds $KillAfterSeconds `
    -StartupMapId $StartupMapId `
    -RootModName $RootModName
if ($LASTEXITCODE -ne 0) {
    throw "Raylib screenshot capture failed with exit code $LASTEXITCODE."
}

$env:LUDOTS_ACCEPTANCE_REQUIRE_RAYLIB_EVIDENCE = "1"
$env:LUDOTS_ACCEPTANCE_SCREENSHOT_PATH = $ScreenshotPath
$env:LUDOTS_ACCEPTANCE_DIAGNOSTIC_PATH = $DiagnosticPath
$env:LUDOTS_ACCEPTANCE_SCREENSHOT_NOT_BEFORE_UTC = $notBeforeUtc

Push-Location $repoRoot
$testExitCode = 0
try {
    dotnet test src\Tests\GasTests\GasTests.csproj -c $Configuration --no-build --filter $TestFilter
    $testExitCode = $LASTEXITCODE
}
finally {
    Pop-Location
    Remove-Item Env:LUDOTS_ACCEPTANCE_REQUIRE_RAYLIB_EVIDENCE -ErrorAction SilentlyContinue
    Remove-Item Env:LUDOTS_ACCEPTANCE_SCREENSHOT_PATH -ErrorAction SilentlyContinue
    Remove-Item Env:LUDOTS_ACCEPTANCE_DIAGNOSTIC_PATH -ErrorAction SilentlyContinue
    Remove-Item Env:LUDOTS_ACCEPTANCE_SCREENSHOT_NOT_BEFORE_UTC -ErrorAction SilentlyContinue
}
if ($testExitCode -ne 0) {
    throw "ItemSystemShowcase acceptance tests failed with exit code $testExitCode."
}

if ($CaptureRoomScreenshots -ne 0 -and $RootModName -eq "ItemSystemShowcaseMod") {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $roomCaptureScript `
        -ScreenshotFrame $ScreenshotFrame `
        -KillAfterSeconds $KillAfterSeconds
    if ($LASTEXITCODE -ne 0) {
        throw "Room screenshot capture failed with exit code $LASTEXITCODE."
    }
}
