param(
    [string]$Configuration = "Debug",
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$project = Join-Path $repoRoot "src\Tests\PersistenceTests\PersistenceTests.csproj"

if (-not (Test-Path $project)) {
    throw "Persistence test project not found: $project"
}

$argsList = @(
    "test",
    $project,
    "-c",
    $Configuration,
    "--filter",
    "SaveSystemUatTests"
)

if ($NoRestore) {
    $argsList += "--no-restore"
}

Push-Location $repoRoot
try {
    & dotnet @argsList
    $exitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($exitCode -ne 0) {
    throw "Save system UAT failed with exit code $exitCode."
}

Write-Host "Save system UAT passed."
Write-Host "artifacts=$(Join-Path $repoRoot 'artifacts\acceptance\save-system')"
