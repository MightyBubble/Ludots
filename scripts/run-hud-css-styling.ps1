param(
    [string]$Configuration = "Debug",
    [switch]$NoBuild
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot "src/Apps/Raylib/Ludots.App.Raylib/Ludots.App.Raylib.csproj"
$appDir = Join-Path $repoRoot "src/Apps/Raylib/Ludots.App.Raylib"

if (-not (Test-Path $appProject)) {
    throw "Raylib app project not found: $appProject"
}

if (-not $NoBuild) {
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
$arguments += 'launcher.hud-css-styling.runtime.json'

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
