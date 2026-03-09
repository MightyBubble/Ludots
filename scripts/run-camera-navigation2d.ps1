Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

& "$PSScriptRoot\run-mod-launcher.ps1" run --preset navigation2d @args
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
