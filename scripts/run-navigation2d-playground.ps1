param(
    [string]$Configuration = 'Release',
    [string]$AppConfig = 'game.navigation2d.json',
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..'
$project = Join-Path $repoRoot 'src\Apps\Raylib\Ludots.App.Raylib\Ludots.App.Raylib.csproj'
$appDir = Split-Path -Parent $project

if (-not (Test-Path $project)) {
    throw "Raylib app project not found: $project"
}

$arguments = @('run', '--project', $project, '-c', $Configuration)
if ($NoBuild) {
    $arguments += '--no-build'
}
$arguments += '--'
$arguments += $AppConfig

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
