param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("U01","U02","U03","U04","U05","U06","U07","U08","U09","U10","U11","U12","U13","U14","U15","U16")]
    [string]$UseCase,
    [string]$OutputRoot = "",
    [switch]$CaptureEvidence,
    [switch]$EditorApplyPatch,
    [switch]$EditorWorkbenchEvidenceOnly,
    [int]$AutoExitFrame = 360,
    [ValidateSet("raylib", "web")]
    [string]$Adapter = "raylib"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\acceptance\mass-navigation-usecases"
}
else {
    $OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$editorCases = @("U01", "U02", "U03", "U09")
$entryMods = @{
    U01 = "MassNavigationU01VisualHeightmapBakeShowcaseMod"
    U02 = "MassNavigationU02LogicHeightmapBakeShowcaseMod"
    U03 = "MassNavigationU03LayerAreaEditorShowcaseMod"
    U04 = "MassNavigationU04PathOnlyQueryShowcaseMod"
    U05 = "MassNavigationU05WorldHpaRouteShowcaseMod"
    U06 = "MassNavigationU06StrategySwitchShowcaseMod"
    U07 = "MassNavigationU07OrderReuseShowcaseMod"
    U08 = "MassNavigationU08TargetAllocationShowcaseMod"
    U09 = "MassNavigationU09LayerCostsShowcaseMod"
    U10 = "MassNavigationU10WaypointAuthoringShowcaseMod"
    U11 = "MassNavigationU11LargeWorldStreamingShowcaseMod"
    U12 = "MassNavigationU12TenKFlowShowcaseMod"
    U13 = "MassNavigationU13StaticObstacleWorldShowcaseMod"
    U14 = "MassNavigationU14PerformanceDebugShowcaseMod"
    U15 = "MassNavigationU15DebugVisualBudgetShowcaseMod"
    U16 = "MassNavigationU16BakeToolQueryShowcaseMod"
}

$caseRoot = Join-Path $OutputRoot $UseCase.ToLowerInvariant()
New-Item -ItemType Directory -Force -Path $caseRoot | Out-Null

if ($editorCases -contains $UseCase) {
    $bakeScript = Join-Path $PSScriptRoot "run-navmesh-bake-raylib-acceptance.ps1"
    $source = switch ($UseCase) {
        "U01" { "vhtm" }
        "U02" { "lhtm" }
        "U03" { "vtxm" }
        "U09" { "lhtm" }
    }
    $mapId = "mass_nav_$($UseCase.ToLowerInvariant())_editor"
    $args = @(
        "-OutputRoot", $caseRoot,
        "-BakeSource", $source,
        "-Preset", "mountainRiver",
        "-WidthChunks", "8",
        "-HeightChunks", "8",
        "-MapId", $mapId,
        "-Layer", "Ground",
        "-Profile", "GroundLight"
    )
    if ($EditorApplyPatch -or $UseCase -eq "U03" -or $UseCase -eq "U09") {
        $args += "-ApplyEditorPatch"
    }
    if (-not $CaptureEvidence -and -not $EditorWorkbenchEvidenceOnly) {
        $args += "-InteractiveWorkbench"
    }

    & powershell -NoProfile -ExecutionPolicy Bypass -File $bakeScript @args
    if ($LASTEXITCODE -ne 0) {
        throw "Editor use case $UseCase failed with exit code $LASTEXITCODE."
    }

    Write-Host "editor_use_case=$UseCase"
    Write-Host "showcase_body=interactive_editor_workbench"
    Write-Host "workbench_screens=$(Join-Path $caseRoot 'screens')"
    if ($CaptureEvidence -or $EditorWorkbenchEvidenceOnly) {
        Write-Host "evidence_mode=auto_capture_only"
        Write-Host "next_human_step=Run again without -CaptureEvidence/-EditorWorkbenchEvidenceOnly to operate the Raylib workbench: 1-5 switches views, left/right click path endpoints, Q/W/E/R/B paints layers, S saves patch + dirty chunks."
    }
    else {
        Write-Host "evidence_mode=human_operated_workbench"
        Write-Host "human_operation=1-5 switch coverage/tile/path/HPA/layer views; left/right click path endpoints; Q/W/E/R/B paint layers; S saves patch + dirty chunks; close the window when done."
    }
    exit 0
}

$entryMod = $entryMods[$UseCase]
if ([string]::IsNullOrWhiteSpace($entryMod)) {
    throw "No entry mod configured for $UseCase"
}

$runScript = Join-Path $repoRoot "scripts\run-mod-launcher.cmd"
$diagnosticPath = Join-Path $caseRoot "$UseCase-raylib-diagnostic.log"
$screenshotPath = Join-Path $caseRoot "$UseCase-framebuffer.png"
$tracePath = Join-Path $caseRoot "$UseCase-operation-trace.jsonl"

if ($CaptureEvidence) {
    if (Test-Path $tracePath) {
        Remove-Item -LiteralPath $tracePath -Force
    }
    if (Test-Path $screenshotPath) {
        Remove-Item -LiteralPath $screenshotPath -Force
    }
    if (Test-Path $diagnosticPath) {
        Remove-Item -LiteralPath $diagnosticPath -Force
    }

    $screenshotFrame = [Math]::Max(300, $AutoExitFrame - 60)
    $env:LUDOTS_TAKE_SCREENSHOT_PATH = $screenshotPath
    $env:LUDOTS_TAKE_SCREENSHOT_FRAME = "$screenshotFrame"
    $env:LUDOTS_AUTO_EXIT_FRAME = "$AutoExitFrame"
    $env:LUDOTS_RAYLIB_DIAGNOSTIC_PATH = $diagnosticPath
    $env:LUDOTS_RAYLIB_TIMING_LOG_INTERVAL_FRAMES = "60"
    $env:LUDOTS_RAYLIB_LIGHTWEIGHT_DIAGNOSTIC_HUD = "0"
    $env:LUDOTS_MASS_NAV_REPLAY_USECASE = $UseCase
    $env:LUDOTS_MASS_NAV_REPLAY_TRACE_PATH = $tracePath
    $env:LUDOTS_MASS_NAV_REPLAY_FRAME_START = "45"
}

try {
    $showcaseBody = if ($UseCase -eq "U16") {
        "runtime_navdata_authoring_update"
    }
    else {
        "interactive_playable_mod"
    }
    Write-Host "playable_use_case=$UseCase"
    Write-Host "entry_mod=$entryMod"
    Write-Host "showcase_body=$showcaseBody"
    if ($CaptureEvidence) {
        Write-Host "evidence_mode=operation_replay_then_capture"
        Write-Host "operation_trace=$tracePath"
    }
    else {
        Write-Host "evidence_mode=human_operated_window"
    }
    & $runScript cli launch "mod:$entryMod" --adapter $Adapter --build never
    if ($LASTEXITCODE -ne 0) {
        throw "Playable use case $UseCase launch failed with exit code $LASTEXITCODE."
    }
}
finally {
    if ($CaptureEvidence) {
        Remove-Item Env:LUDOTS_TAKE_SCREENSHOT_PATH -ErrorAction SilentlyContinue
        Remove-Item Env:LUDOTS_TAKE_SCREENSHOT_FRAME -ErrorAction SilentlyContinue
        Remove-Item Env:LUDOTS_AUTO_EXIT_FRAME -ErrorAction SilentlyContinue
        Remove-Item Env:LUDOTS_RAYLIB_DIAGNOSTIC_PATH -ErrorAction SilentlyContinue
        Remove-Item Env:LUDOTS_RAYLIB_TIMING_LOG_INTERVAL_FRAMES -ErrorAction SilentlyContinue
        Remove-Item Env:LUDOTS_RAYLIB_LIGHTWEIGHT_DIAGNOSTIC_HUD -ErrorAction SilentlyContinue
        Remove-Item Env:LUDOTS_MASS_NAV_REPLAY_USECASE -ErrorAction SilentlyContinue
        Remove-Item Env:LUDOTS_MASS_NAV_REPLAY_TRACE_PATH -ErrorAction SilentlyContinue
        Remove-Item Env:LUDOTS_MASS_NAV_REPLAY_FRAME_START -ErrorAction SilentlyContinue
    }
}

if ($CaptureEvidence) {
    if (-not (Test-Path $screenshotPath)) {
        throw "Missing framebuffer evidence: $screenshotPath"
    }

    if (-not (Test-Path $diagnosticPath)) {
        throw "Missing Raylib diagnostic log: $diagnosticPath"
    }

    if (-not (Test-Path $tracePath)) {
        throw "Missing operation replay trace: $tracePath"
    }

    $traceText = Get-Content -LiteralPath $tracePath -Raw
    if ($traceText -notmatch '"kind":"input"' -or $traceText -notmatch '"kind":"result"' -or $traceText -notmatch '"kind":"complete"') {
        throw "Operation trace for $UseCase did not include input/result/complete events: $tracePath"
    }
}

Write-Host "playable_use_case=$UseCase"
Write-Host "entry_mod=$entryMod"
Write-Host "output=$caseRoot"
