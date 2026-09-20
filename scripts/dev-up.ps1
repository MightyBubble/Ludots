# 作者弱网一键入口（Windows PowerShell / 跨平台 pwsh）：不访问 nuget.org，只用仓库内 external/nuget。
# 依赖：已安装 .NET 9 SDK。Raylib CLI 路径不需要 Node。
# 用法：
#   .\scripts\dev-up.ps1
#   .\scripts\dev-up.ps1 launch mod:X --adapter raylib
#   .\scripts\dev-up.ps1 build-only
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ArgsList = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location $repoRoot

if (-not (Test-Path (Join-Path $repoRoot 'nuget.config')) -or -not (Test-Path (Join-Path $repoRoot 'external/nuget'))) {
    throw "offline nuget layer missing (nuget.config + external/nuget/). Refusing to fall back to nuget.org."
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw "dotnet SDK not found on PATH. Install .NET 9 SDK first."
}

$sdk9 = & dotnet --list-sdks | Where-Object { $_ -match '^9\.' } | Select-Object -First 1
if (-not $sdk9) {
    throw ".NET 9 SDK required (global.json). Run: dotnet --list-sdks"
}

$tfm = 'net9.0'
$cliProj = Join-Path $repoRoot 'src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj'
$cliDll = Join-Path $repoRoot "src/Tools/Ludots.Launcher.Cli/bin/Release/$tfm/Ludots.Launcher.Cli.dll"
$appProj = Join-Path $repoRoot 'src/Apps/Raylib/Ludots.App.Raylib/Ludots.App.Raylib.csproj'
$defaultSelector = @('mod:LudotsCoreMod', 'mod:ExampleMod')

$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

Write-Host "== Ludots dev-up (offline nuget) =="
Write-Host "repo: $repoRoot"
Write-Host "sdk:  $sdk9"

function Build-Stack {
    Write-Host "restore+build: Launcher.Cli"
    & dotnet build $cliProj -c Release -v q
    if ($LASTEXITCODE -ne 0) { throw "Launcher.Cli build failed" }
    Write-Host "restore+build: App.Raylib"
    & dotnet build $appProj -c Release -v q
    if ($LASTEXITCODE -ne 0) { throw "App.Raylib build failed" }
    Write-Host "restore+build: LudotsCoreMod + ExampleMod"
    & dotnet build (Join-Path $repoRoot 'mods/LudotsCoreMod/LudotsCoreMod.csproj') -c Release -v q
    if ($LASTEXITCODE -ne 0) { throw "LudotsCoreMod build failed" }
    & dotnet build (Join-Path $repoRoot 'mods/ExampleMod/ExampleMod.csproj') -c Release -v q
    if ($LASTEXITCODE -ne 0) { throw "ExampleMod build failed" }
    if (-not (Test-Path $cliDll)) { throw "launcher DLL missing after build: $cliDll" }
}

$action = if ($ArgsList.Count -gt 0) { $ArgsList[0] } else { 'launch' }

if ($action -eq 'build-only') {
    Build-Stack
    Write-Host 'done: build-only'
    exit 0
}

Build-Stack

$launcherArgs = @()
if ($ArgsList.Count -gt 0) {
    $launcherArgs = @($ArgsList)
} else {
    $launcherArgs = @('launch') + $defaultSelector + @('--adapter', 'raylib')
}

if ($launcherArgs.Count -eq 1 -and $launcherArgs[0] -in @('launch', 'resolve', 'build')) {
    $launcherArgs = @($launcherArgs[0]) + $defaultSelector + @('--adapter', 'raylib')
}

Write-Host ("dotnet {0} {1}" -f $cliDll, ($launcherArgs -join ' '))
& dotnet $cliDll @launcherArgs
exit $LASTEXITCODE
