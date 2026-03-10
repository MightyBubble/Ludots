Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Set-Location $PSScriptRoot

$forwardArgs = @($args)
if ($forwardArgs.Count -ge 2 -and $forwardArgs[0] -eq "--" -and $forwardArgs[1] -eq "cli")
{
    Write-Warning "Normalize wrapper args: use '.\\scripts\\run-mod-launcher.cmd cli ...' instead of '.\\scripts\\run-mod-launcher.cmd -- cli ...'."
    $forwardArgs = $forwardArgs[1..($forwardArgs.Count - 1)]
}

dotnet run --project ..\src\Tools\ModLauncher\Ludots.ModLauncher.csproj -c Release -- @forwardArgs

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
