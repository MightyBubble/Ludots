Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Set-Location $PSScriptRoot

dotnet run --project ..\src\Tools\ModLauncher\Ludots.ModLauncher.csproj -c Release -- @args

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
