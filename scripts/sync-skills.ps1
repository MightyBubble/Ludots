[CmdletBinding()]
param(
    [ValidateSet('all', 'codex', 'claude', 'pi', 'dsh', 'zcode')]
    [string]$Target = 'all',
    [string]$CodexRoot = (Join-Path $HOME '.codex/skills'),
    [string]$ClaudeRoot = (Join-Path $HOME '.claude/skills'),
    [string]$PiRoot = (Join-Path $HOME '.pi/agent/skills'),
    [string]$DshRoot = (Join-Path $HOME '.dsh/skills'),
    [string]$ZcodeRoot = (Join-Path $HOME '.agents/skills'),
    [switch]$SkipValidation
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptDir '..'))
$registryPath = Join-Path $repoRoot 'skills/registry.json'
$validationScript = Join-Path $scriptDir 'validate-skills.ps1'

if (-not $SkipValidation) {
    & $validationScript
}

$registry = Get-Content -Raw -Encoding UTF8 $registryPath | ConvertFrom-Json

function Sync-TargetRoot {
    param(
        [string]$Name,
        [string]$Root
    )

    New-Item -ItemType Directory -Force -Path $Root | Out-Null
    $manifestPath = Join-Path $Root '.ludots-managed-skills.json'
    $previousInstallNames = @()

    if (Test-Path $manifestPath) {
        try {
            $manifest = Get-Content -Raw -Encoding UTF8 $manifestPath | ConvertFrom-Json
            $previousInstallNames = @($manifest.install_names)
        } catch {
            Write-Host "Ignoring unreadable manifest at $manifestPath" -ForegroundColor Yellow
        }
    }

    $currentInstallNames = New-Object System.Collections.Generic.List[string]

    foreach ($skill in @($registry.skills)) {
        $source = Join-Path $repoRoot $skill.path
        $destination = Join-Path $Root $skill.install_name
        if (Test-Path $destination) {
            Remove-Item -Path $destination -Recurse -Force
        }

        Copy-Item -Path $source -Destination $destination -Recurse -Force
        $currentInstallNames.Add($skill.install_name) | Out-Null
    }

    foreach ($stale in $previousInstallNames) {
        if ($currentInstallNames -contains $stale) {
            continue
        }

        $stalePath = Join-Path $Root $stale
        if (Test-Path $stalePath) {
            Remove-Item -Path $stalePath -Recurse -Force
        }
    }

    $manifest = [PSCustomObject]@{
        managed_by = 'Ludots shared skills'
        source_repo = $repoRoot
        synced_at_utc = [DateTime]::UtcNow.ToString('o')
        install_names = @($currentInstallNames)
    }

    $manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding UTF8
    Write-Host "Synced $($currentInstallNames.Count) skills to $Name => $Root" -ForegroundColor Green
}

switch ($Target) {
    'all' {
        Sync-TargetRoot -Name 'Codex' -Root $CodexRoot
        Sync-TargetRoot -Name 'Claude' -Root $ClaudeRoot
        Sync-TargetRoot -Name 'Pi' -Root $PiRoot
        Sync-TargetRoot -Name 'Dsh' -Root $DshRoot
        Sync-TargetRoot -Name 'Zcode' -Root $ZcodeRoot
    }
    'codex' {
        Sync-TargetRoot -Name 'Codex' -Root $CodexRoot
    }
    'claude' {
        Sync-TargetRoot -Name 'Claude' -Root $ClaudeRoot
    }
    'pi' {
        Sync-TargetRoot -Name 'Pi' -Root $PiRoot
    }
    'dsh' {
        Sync-TargetRoot -Name 'Dsh' -Root $DshRoot
    }
    'zcode' {
        Sync-TargetRoot -Name 'Zcode' -Root $ZcodeRoot
    }
}
