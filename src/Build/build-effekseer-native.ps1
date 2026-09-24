[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$EffekseerRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$nativeProject = Join-Path $repoRoot 'src\Native\Ludots.Effekseer.Native'
$workRoot = Join-Path $repoRoot 'output\effekseer-native'
$buildRoot = Join-Path $workRoot "build-$RuntimeIdentifier"
$runtimeRoot = Join-Path $repoRoot "src\Libraries\Effekseer\runtimes\$RuntimeIdentifier\native"
$vendoredRoot = Join-Path $repoRoot 'src\Libraries\Effekseer\upstream'

if ($RuntimeIdentifier -ne 'win-x64') {
    throw "The first production slice supports only win-x64; requested '$RuntimeIdentifier'."
}

New-Item -ItemType Directory -Force $workRoot | Out-Null

if ([string]::IsNullOrWhiteSpace($EffekseerRoot)) {
    $EffekseerRoot = $vendoredRoot
}

$EffekseerRoot = (Resolve-Path -LiteralPath $EffekseerRoot).Path
if (-not (Test-Path -LiteralPath (Join-Path $EffekseerRoot 'src\Effekseer\Effekseer.h'))) {
    throw "Effekseer 1.80.6 vendored source is missing or incomplete at '$EffekseerRoot'."
}

cmake -S $nativeProject -B $buildRoot -G 'Visual Studio 17 2022' -A x64 `
    "-DEFFEKSEER_ROOT=$EffekseerRoot"
if ($LASTEXITCODE -ne 0) { throw "Effekseer CMake generation failed with exit code $LASTEXITCODE." }

cmake --build $buildRoot --config $Configuration --target ludots_effekseer
if ($LASTEXITCODE -ne 0) { throw "Effekseer native build failed with exit code $LASTEXITCODE." }

$builtLibrary = Join-Path $buildRoot "$Configuration\ludots_effekseer.dll"
if (-not (Test-Path -LiteralPath $builtLibrary)) {
    throw "Native build completed without '$builtLibrary'."
}

New-Item -ItemType Directory -Force $runtimeRoot | Out-Null
Copy-Item -LiteralPath $builtLibrary -Destination (Join-Path $runtimeRoot 'ludots_effekseer.dll') -Force
Write-Host "Effekseer native runtime: $(Join-Path $runtimeRoot 'ludots_effekseer.dll')"
