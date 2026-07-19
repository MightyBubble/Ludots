param(
    [string]$ScreenshotPath = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path "artifacts\acceptance\item-loadout-showcase\item-loadout-showcase-raylib.png"),
    [string]$DiagnosticPath = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path "artifacts\acceptance\item-loadout-showcase\item-loadout-showcase-raylib-diagnostic.log"),
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
    -RootModName "ItemLoadoutShowcaseMod" `
    -TestFilter "FullyQualifiedName~ItemLoadoutShowcaseMod_StartsInLoadoutGarageWithoutCrossRoomNavigation" `
    -StartupMapId "item_system_showcase_loadout_garage"

if ($LASTEXITCODE -ne 0) {
    throw "Item loadout showcase acceptance failed with exit code $LASTEXITCODE."
}
