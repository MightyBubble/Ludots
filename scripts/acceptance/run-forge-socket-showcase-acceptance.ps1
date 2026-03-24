param(
    [string]$ScreenshotPath = "D:\001_AI\LudotsDev\Ludots-item-worktree\artifacts\acceptance\forge-socket-showcase\forge-socket-showcase-raylib.png",
    [string]$DiagnosticPath = "D:\001_AI\LudotsDev\Ludots-item-worktree\artifacts\acceptance\forge-socket-showcase\forge-socket-showcase-raylib-diagnostic.log",
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
    -RootModName "ForgeSocketShowcaseMod" `
    -TestFilter "FullyQualifiedName~ForgeSocketShowcaseMod_StartsInForgeLabWithoutCrossRoomNavigation" `
    -StartupMapId "item_system_showcase_forge_socket_lab"

if ($LASTEXITCODE -ne 0) {
    throw "Forge socket showcase acceptance failed with exit code $LASTEXITCODE."
}
