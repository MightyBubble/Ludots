[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = '',
    [string]$EffekseerRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$nativeProject = Join-Path $repoRoot 'src\Native\Ludots.Effekseer.Native'
$workRoot = Join-Path $repoRoot 'output\effekseer-native'
$librariesRoot = Join-Path $repoRoot 'src\Libraries\Effekseer'
$contractPath = Join-Path $librariesRoot 'runtime-contract.json'
$upstreamPath = Join-Path $librariesRoot 'Effekseer.upstream.json'
$vendoredRoot = Join-Path $repoRoot 'src\Libraries\Effekseer\upstream'
$runtimeContractScript = Join-Path $repoRoot 'scripts\effekseer-runtime-contract.mjs'

$contract = Get-Content -LiteralPath $contractPath -Raw -Encoding UTF8 | ConvertFrom-Json
$upstream = Get-Content -LiteralPath $upstreamPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
    $declaredRuntimes = @($contract.runtimes)
    if ($declaredRuntimes.Count -ne 1) {
        throw "RuntimeIdentifier is required when the Effekseer contract declares $($declaredRuntimes.Count) runtimes."
    }
    $RuntimeIdentifier = [string]$declaredRuntimes[0].runtimeIdentifier
}
$buildRoot = Join-Path $workRoot "build-$RuntimeIdentifier"
$runtimeEntries = @($contract.runtimes | Where-Object { $_.runtimeIdentifier -eq $RuntimeIdentifier })
if ($runtimeEntries.Count -ne 1) {
    throw "Effekseer runtime contract must contain exactly one entry for '$RuntimeIdentifier'; found $($runtimeEntries.Count)."
}
$runtime = $runtimeEntries[0]
$runtimeRoot = Join-Path $librariesRoot "runtimes\$RuntimeIdentifier"
$runtimeLibrary = Join-Path $runtimeRoot ([string]$runtime.library).Replace('/', '\')
$libraryFileName = [IO.Path]::GetFileName($runtimeLibrary)
$manifestPath = Join-Path $runtimeRoot 'runtime-manifest.json'

New-Item -ItemType Directory -Force $workRoot | Out-Null

if ([string]::IsNullOrWhiteSpace($EffekseerRoot)) {
    $EffekseerRoot = $vendoredRoot
}

$EffekseerRoot = (Resolve-Path -LiteralPath $EffekseerRoot).Path
if (-not (Test-Path -LiteralPath (Join-Path $EffekseerRoot 'src\Effekseer\Effekseer.h'))) {
    throw "Effekseer $($upstream.version) vendored source is missing or incomplete at '$EffekseerRoot'."
}

& node $runtimeContractScript --verify-vendored-source $EffekseerRoot
if ($LASTEXITCODE -ne 0) {
    throw "Effekseer vendored source provenance verification failed with exit code $LASTEXITCODE."
}

cmake -S $nativeProject -B $buildRoot -G 'Visual Studio 17 2022' -A x64 `
    "-DEFFEKSEER_ROOT=$EffekseerRoot" `
    "-DLUDOTS_EFFEKSEER_BRIDGE_ABI_VERSION=$($contract.bridgeAbiVersion)" `
    "-DLUDOTS_EFFEKSEER_RUNTIME_FORMAT_VERSION=$($contract.runtimeFormatVersion)" `
    "-DLUDOTS_EFFEKSEER_DYNAMIC_INPUT_SLOT_COUNT=$($contract.dynamicInputSlotCount)" `
    "-DLUDOTS_EFFEKSEER_VERSION=$($upstream.version)"
if ($LASTEXITCODE -ne 0) { throw "Effekseer CMake generation failed with exit code $LASTEXITCODE." }

cmake --build $buildRoot --config $Configuration --target ludots_effekseer
if ($LASTEXITCODE -ne 0) { throw "Effekseer native build failed with exit code $LASTEXITCODE." }

$builtLibrary = Join-Path $buildRoot "$Configuration\$libraryFileName"
if (-not (Test-Path -LiteralPath $builtLibrary)) {
    throw "Native build completed without '$builtLibrary'."
}

New-Item -ItemType Directory -Force (Split-Path -Parent $runtimeLibrary) | Out-Null
Copy-Item -LiteralPath $builtLibrary -Destination $runtimeLibrary -Force
$sha256 = (Get-FileHash -LiteralPath $runtimeLibrary -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = [ordered]@{
    runtimeIdentifier = $RuntimeIdentifier
    library = ([string]$runtime.library).Replace('\', '/')
    bridgeAbiVersion = [int]$contract.bridgeAbiVersion
    effekseerVersion = [string]$upstream.version
    effekseerCommit = [string]$upstream.commit
    effekseerVendoredTreeSha256 = [string]$upstream.vendoredTreeSha256
    effekseerVendoredFileCount = [int]$upstream.vendoredFileCount
    effekseerRuntimeFormatVersion = [int]$contract.runtimeFormatVersion
    dynamicInputSlotCount = [int]$contract.dynamicInputSlotCount
    emitterNodeTypes = $contract.emitterNodeTypes
    sha256 = $sha256
}
$manifestJson = $manifest | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText(
    $manifestPath,
    $manifestJson + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))

Write-Host "Effekseer native runtime: $runtimeLibrary"
Write-Host "Effekseer runtime manifest: $manifestPath"
