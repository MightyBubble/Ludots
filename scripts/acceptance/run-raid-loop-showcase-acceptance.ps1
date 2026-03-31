param(
    [string]$ScreenshotPath = "D:\001_AI\LudotsDev\Ludots-item-worktree\artifacts\acceptance\raid-loop-showcase\raid-loop-showcase-raylib.png",
    [string]$DiagnosticPath = "D:\001_AI\LudotsDev\Ludots-item-worktree\artifacts\acceptance\raid-loop-showcase\raid-loop-showcase-raylib-diagnostic.log",
    [int]$ScreenshotFrame = 180,
    [int]$KillAfterSeconds = 45,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = "D:\001_AI\LudotsDev\Ludots-item-worktree"
$scriptPath = Join-Path $repoRoot "scripts\acceptance\run-item-system-showcase-acceptance.ps1"

& powershell -NoProfile -ExecutionPolicy Bypass -File $scriptPath `
    -ScreenshotPath $ScreenshotPath `
    -DiagnosticPath $DiagnosticPath `
    -ScreenshotFrame $ScreenshotFrame `
    -KillAfterSeconds $KillAfterSeconds `
    -Configuration $Configuration `
    -CaptureRoomScreenshots 0 `
    -RootModName "RaidLoopShowcaseMod" `
    -TestFilter "FullyQualifiedName~RaidLoopShowcaseMod_StartsInRaidLoopWithoutCrossRoomNavigation" `
    -StartupMapId "item_system_showcase_raid_loop"

if ($LASTEXITCODE -ne 0) {
    throw "Raid loop showcase acceptance failed with exit code $LASTEXITCODE."
}
