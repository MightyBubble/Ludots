param(
    [string]$Configuration = 'Release',
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..'
$appProject = Join-Path $repoRoot 'src\Apps\Raylib\Ludots.App.Raylib\Ludots.App.Raylib.csproj'
$appDir = Split-Path -Parent $appProject
$modsToBuild = @(
    (Join-Path $repoRoot 'mods\EntityCommandPanelMod\EntityCommandPanelMod.csproj'),
    (Join-Path $repoRoot 'mods\capabilities\entityinfo\EntityInfoPanelsMod\EntityInfoPanelsMod.csproj'),
    (Join-Path $repoRoot 'mods\showcases\interaction\InteractionShowcaseMod\InteractionShowcaseMod.csproj'),
    (Join-Path $repoRoot 'mods\showcases\entity_command_panel\EntityCommandPanelShowcaseMod\EntityCommandPanelShowcaseMod.csproj')
)

if (-not (Test-Path $appProject)) {
    throw "Raylib app project not found: $appProject"
}

foreach ($modProject in $modsToBuild) {
    if (-not (Test-Path $modProject)) {
        throw "Mod project not found: $modProject"
    }
}

if (-not $NoBuild) {
    foreach ($modProject in $modsToBuild) {
        & dotnet build $modProject -c $Configuration -nologo
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }

    & dotnet build $appProject -c $Configuration -nologo
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

$arguments = @(
    'run',
    '--project', $appProject,
    '-c', $Configuration
)

if ($NoBuild) {
    $arguments += '--no-build'
}

$arguments += '--'
$arguments += 'launcher.entity-command-panel-showcase.runtime.json'

Push-Location $appDir
try {
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}
