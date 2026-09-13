param(
    [switch]$NoInstall,
    [switch]$NoBrowser,
    [switch]$Headless
)

$ErrorActionPreference = 'Stop'
$studio = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "run-authoring-studio.ps1"
& $studio @PSBoundParameters
exit $LASTEXITCODE
