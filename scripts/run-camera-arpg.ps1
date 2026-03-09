Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

& "$PSScriptRoot\run-mod-launcher.ps1" run --preset arpg @args
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
