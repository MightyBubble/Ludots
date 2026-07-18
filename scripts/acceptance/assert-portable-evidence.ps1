[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Root
)

$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "MassNavigationEvidencePortability.psm1") -Force

$violations = @(Get-EvidenceAbsolutePathViolations -Root $Root)
if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "Portable evidence validation passed: $Root"
