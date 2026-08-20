param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ArgsList
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($null -eq $ArgsList) {
    $ArgsList = @()
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptDir ".."))
$launchScript = Join-Path $repoRoot "src/Tools/Ludots.Pi/scripts/launch.mjs"

if (-not (Test-Path $launchScript)) {
    throw "Ludots Pi launcher is missing: $launchScript"
}

$env:LUDOTS_PI_WORKSPACE = $repoRoot
node $launchScript @ArgsList
exit $LASTEXITCODE
