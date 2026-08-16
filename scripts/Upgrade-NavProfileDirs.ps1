param(
    [string]$RepoRoot = (Resolve-Path ".").Path,
    [string]$NavMeshConfigPath = "assets/Configs/Navigation/navmesh.json",
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

function Get-SafeSegment([string]$raw) {
    if ($null -eq $raw) { return "null" }
    $raw = $raw.Trim()
    if ($raw.Length -eq 0) { return "empty" }
    $chars = $raw.ToCharArray()
    for ($i = 0; $i -lt $chars.Length; $i++) {
        $c = $chars[$i]
        $ok = (($c -ge 'a' -and $c -le 'z') -or ($c -ge 'A' -and $c -le 'Z') -or ($c -ge '0' -and $c -le '9') -or $c -eq '_' -or $c -eq '-')
        if (-not $ok) { $chars[$i] = '_' }
    }
    return -join $chars
}

$cfgFile = Join-Path $RepoRoot $NavMeshConfigPath
if (-not (Test-Path -LiteralPath $cfgFile)) {
    throw "NavMesh config not found: $cfgFile"
}

$cfg = Get-Content -LiteralPath $cfgFile -Raw | ConvertFrom-Json
if ($null -eq $cfg.profiles -or $cfg.profiles.Count -lt 1) {
    throw "NavMesh config has no profiles: $cfgFile"
}

$profileIds = @()
foreach ($p in $cfg.profiles) {
    if ($null -eq $p.id -or [string]::IsNullOrWhiteSpace([string]$p.id)) { throw "NavMesh profile id is required." }
    $profileIds += [string]$p.id
}

$navRoots = New-Object System.Collections.Generic.List[string]

$coreNav = Join-Path $RepoRoot "assets\Data\Nav"
if (Test-Path -LiteralPath $coreNav) { $navRoots.Add($coreNav) }

$srcMods = Join-Path $RepoRoot "src\Mods"
if (Test-Path -LiteralPath $srcMods) {
    foreach ($m in Get-ChildItem -LiteralPath $srcMods -Directory) {
        $p = Join-Path $m.FullName "assets\Data\Nav"
        if (Test-Path -LiteralPath $p) { $navRoots.Add($p) }
    }
}

$assetsMods = Join-Path $RepoRoot "assets\Mods"
if (Test-Path -LiteralPath $assetsMods) {
    foreach ($m in Get-ChildItem -LiteralPath $assetsMods -Directory) {
        $p = Join-Path $m.FullName "assets\Data\Nav"
        if (Test-Path -LiteralPath $p) { $navRoots.Add($p) }
    }
}

if ($navRoots.Count -eq 0) {
    Write-Host "No assets\\Data\\Nav roots found under repo. Nothing to migrate."
    exit 0
}

$migrated = 0
$skipped = 0

foreach ($root in $navRoots) {
    Write-Host "Scanning: $root"
    foreach ($mapDir in Get-ChildItem -LiteralPath $root -Directory) {
        foreach ($layerDir in Get-ChildItem -LiteralPath $mapDir.FullName -Directory -Filter "layer*") {
            foreach ($legacy in Get-ChildItem -LiteralPath $layerDir.FullName -Directory -Filter "profile*") {
                if ($legacy.Name -notmatch '^profile(\d+)$') { continue }
                $idx = [int]$Matches[1]
                if ($idx -lt 0 -or $idx -ge $profileIds.Count) {
                    throw "Found legacy dir with unknown profile index: $($legacy.FullName)"
                }
                $profileId = $profileIds[$idx]
                $targetName = "profile_{0}" -f (Get-SafeSegment $profileId)
                $targetPath = Join-Path $layerDir.FullName $targetName

                if ((Resolve-Path -LiteralPath $legacy.FullName).Path -eq (Resolve-Path -LiteralPath $targetPath -ErrorAction SilentlyContinue).Path) {
                    $skipped++
                    continue
                }

                if (Test-Path -LiteralPath $targetPath) {
                    $hasFiles = @(Get-ChildItem -LiteralPath $legacy.FullName -Recurse -File -ErrorAction SilentlyContinue).Count -gt 0
                    if ($hasFiles) { throw "Target exists; refusing to merge: $targetPath" }
                    $skipped++
                    continue
                }

                if ($WhatIf) {
                    Write-Host "WhatIf: Move '$($legacy.FullName)' -> '$targetPath'"
                } else {
                    Move-Item -LiteralPath $legacy.FullName -Destination $targetPath
                }
                $migrated++
            }
        }
    }
}

Write-Host "Done. migrated=$migrated skipped=$skipped"
exit 0

