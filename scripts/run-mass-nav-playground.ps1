param(
    [string]$Configuration = 'Release',
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..'
$project = Join-Path $repoRoot 'src\Apps\Raylib\Ludots.App.Raylib\Ludots.App.Raylib.csproj'
$appDir = Split-Path -Parent $project

$arguments = @('run', '--project', $project, '-c', $Configuration)
if ($NoBuild)
{
    $arguments += '--no-build'
}

$arguments += '--'
$arguments += 'launcher.mass-nav.runtime.json'

Push-Location $appDir
try
{
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
}
finally
{
    Pop-Location
}
