param(
    [Parameter(Mandatory)][string]$RunName,
    [int]$Frames = 600,
    [int]$Port = 47931,
    [int]$TimingInterval = 1,
    [ValidateSet('on','off')][string]$Shadows = 'off'
)
$ErrorActionPreference = 'Stop'
$benchRoot = $PSScriptRoot
$repoRoot = [IO.Path]::GetFullPath((Join-Path $benchRoot '../../..'))
$appRoot = Join-Path $repoRoot 'src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net9.0'
$graphPath = Join-Path $repoRoot 'artifacts/launcher/raylib.launch.graph.json'
$plan = Get-Content -LiteralPath $graphPath -Raw | ConvertFrom-Json
@{
    LaunchGraphPath = $graphPath
    LaunchGraphFullPath = $graphPath
    PlanSelectors = $plan.selectors
    PlanRootModIds = $plan.rootModIds
    PlanOrderedModIds = $plan.orderedModIds
    PlanFingerprint = $plan.planFingerprint
    PlanSchemaVersion = $plan.schemaVersion
    PlanGeneratedAtUtc = $plan.generatedAtUtc
    BrowserRuntime = $plan.browserRuntime
} | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $appRoot 'launcher.runtime.json')
if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
    throw "Bridge port $Port is already in use."
}
$env:LUDOTS_AGENT_BRIDGE_PORT = [string]$Port
$env:LUDOTS_RAYLIB_DRAW_SHADOWS = ($Shadows -eq 'on').ToString().ToLowerInvariant()
$env:LUDOTS_RAYLIB_DIAGNOSTIC_PATH = Join-Path $benchRoot "$RunName-timing.log"
$env:LUDOTS_RAYLIB_TIMING_LOG_INTERVAL_FRAMES = [string]$TimingInterval
$env:LUDOTS_AUTO_EXIT_FRAME = [string]$Frames
$env:LUDOTS_TAKE_SCREENSHOT_PATH = Join-Path $benchRoot "$RunName.png"
$env:LUDOTS_TAKE_SCREENSHOT_FRAME = '120'
$process = Start-Process -FilePath (Join-Path $appRoot 'Ludots.App.Raylib.exe') `
    -ArgumentList 'launcher.runtime.json' -WorkingDirectory $appRoot -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $benchRoot "$RunName-stdout.log") `
    -RedirectStandardError (Join-Path $benchRoot "$RunName-stderr.log") -PassThru
@{
    pid = $process.Id
    executable = $process.Path
    port = $Port
    frames = $Frames
    startedUtc = [DateTime]::UtcNow.ToString('O')
    commit = (git -C $repoRoot rev-parse HEAD)
    configuration = 'Release'
    shadows = $Shadows
    timingInterval = $TimingInterval
    tieredCompilation = $env:DOTNET_TieredCompilation
    cpu = (Get-CimInstance Win32_Processor | Select-Object Name,NumberOfCores,NumberOfLogicalProcessors)
    gpu = (Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion)
    launchPlanFingerprint = $plan.planFingerprint
} | ConvertTo-Json | Set-Content (Join-Path $benchRoot "$RunName-process.json")
$process | Select-Object Id, Path
