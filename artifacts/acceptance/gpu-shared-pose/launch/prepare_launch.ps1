param(
    [ValidateSet('prepare','build-mods','build-app','verify','start')]
    [string]$Action = 'prepare'
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
python (Join-Path $scriptDir 'prepare_launch.py') $Action
if ($LASTEXITCODE -ne 0) { throw "GPU shared-pose launch action failed: $Action" }
