param(
    [string]$ScreenshotPath = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path "artifacts\acceptance\weapon-bench-showcase\weapon-bench-showcase-raylib.png"),
    [string]$DiagnosticPath = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path "artifacts\acceptance\weapon-bench-showcase\weapon-bench-showcase-raylib-diagnostic.log"),
    [int]$ScreenshotFrame = 180,
    [int]$KillAfterSeconds = 45,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$scriptPath = Join-Path $repoRoot "scripts\acceptance\run-item-system-showcase-acceptance.ps1"

& powershell -NoProfile -ExecutionPolicy Bypass -File $scriptPath `
    -ScreenshotPath $ScreenshotPath `
    -DiagnosticPath $DiagnosticPath `
    -ScreenshotFrame $ScreenshotFrame `
    -KillAfterSeconds $KillAfterSeconds `
    -Configuration $Configuration `
    -CaptureRoomScreenshots 0 `
    -RootModName "WeaponBenchShowcaseMod" `
    -TestFilter "FullyQualifiedName~WeaponBenchShowcaseMod_StartsInWeaponBenchWithoutCrossRoomNavigation" `
    -StartupMapId "item_system_showcase_weapon_bench"

if ($LASTEXITCODE -ne 0) {
    throw "Weapon bench showcase acceptance failed with exit code $LASTEXITCODE."
}
