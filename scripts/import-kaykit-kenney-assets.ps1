<#
.SYNOPSIS
  Downloads the CC0 KayKit and Kenney model packs into
  mods/KayKitKenneyAssetPackMod/assets/Models and regenerates the
  Presentation/*.json host-asset registries using the same convention as
  PerformerBlacksmithShowcaseMod.

.EXAMPLE
  .\scripts\import-kaykit-kenney-assets.ps1
  .\scripts\import-kaykit-kenney-assets.ps1 -Force
#>
[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$modRoot = Join-Path $repoRoot 'mods\KayKitKenneyAssetPackMod'
$packsPath = Join-Path $modRoot 'packs.json'

if (-not (Test-Path -LiteralPath $packsPath)) {
    throw "KayKit/Kenney pack manifest not found: $packsPath"
}

$packs = (Get-Content -LiteralPath $packsPath -Raw | ConvertFrom-Json).packs

function Download-File {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url,
        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    # Prefer curl.exe: it is present on Windows 10+ and avoids IE engine issues
    # that sometimes affect Invoke-WebRequest in restricted shells.
    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($curl) {
        & $curl.Source -L --fail --silent --show-error $Url -o $Destination
        if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $Destination)) {
            return
        }
    }

    try {
        Invoke-WebRequest -Uri $Url -OutFile $Destination -UseBasicParsing
    }
    catch {
        # PowerShell 7 does not need -UseBasicParsing; retry without it.
        Invoke-WebRequest -Uri $Url -OutFile $Destination
    }
}

function ConvertTo-JsonArray {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Items,
        [string]$SortProperty
    )

    if ($Items.Count -eq 0) {
        return '[]'
    }

    $sorted = @($Items)
    if ($SortProperty) {
        $sorted = @($sorted | Sort-Object $SortProperty)
    }

    $parts = foreach ($item in $sorted) {
        $item | ConvertTo-Json -Depth 6
    }

    return '[' + (($parts | ForEach-Object { $_ }) -join ',') + ']'
}


function Import-Pack {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Pack
    )

    $targetDir = Join-Path $modRoot $Pack.targetDir
    if ((Test-Path -LiteralPath $targetDir) -and -not $Force) {
        $existing = Get-ChildItem -LiteralPath $targetDir -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension -in '.gltf', '.glb' }
        if ($existing) {
            Write-Host "Skipping $($Pack.id): models already exist in $($Pack.targetDir). Use -Force to re-import."
            return
        }
    }

    Write-Host "Downloading $($Pack.name)..."
    $zipPath = Join-Path $env:TEMP "$($Pack.id).zip"
    try {
        Download-File -Url $Pack.sourceUrl -Destination $zipPath
    }
    catch {
        Write-Host "Primary URL failed, trying fallback..."
        Download-File -Url $Pack.sourceUrlFallback -Destination $zipPath
    }

    $extractRoot = Join-Path $env:TEMP "$($Pack.id)-extracted"
    if (Test-Path -LiteralPath $extractRoot) {
        Remove-Item -LiteralPath $extractRoot -Recurse -Force
    }
    Expand-Archive -LiteralPath $zipPath -DestinationPath $extractRoot -Force
    Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue

    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    $modelFiles = Get-ChildItem -LiteralPath $extractRoot -Recurse -File |
        Where-Object { $_.Extension -in '.gltf', '.glb', '.png' }

    foreach ($file in $modelFiles) {
        $dest = Join-Path $targetDir $file.Name
        Copy-Item -LiteralPath $file.FullName -Destination $dest -Force
    }

    Write-Host "Imported $($modelFiles.Count) files for $($Pack.id) into $($Pack.targetDir)."
    Remove-Item -LiteralPath $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
}

function Update-PresentationRegistries {
    $modelsRoot = Join-Path $modRoot 'assets\Models'
    $presentationRoot = Join-Path $modRoot 'assets\Presentation'
    New-Item -ItemType Directory -Path $modelsRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $presentationRoot -Force | Out-Null

    $modelFiles = Get-ChildItem -LiteralPath $modelsRoot -Recurse -File |
        Where-Object { $_.Extension -in '.gltf', '.glb' }

    $meshAssets = @()
    $hostAssets = @()

    foreach ($file in $modelFiles) {
        $relative = $file.FullName.Substring($modelsRoot.Length).TrimStart('\', '/')
        $relativeUri = $relative.Replace('\', '/')
        $stem = $relativeUri.Substring(0, $relativeUri.LastIndexOf('.'))
        $id = ('kaykitkenney.' + ($stem -replace '[/\\]', '.').ToLowerInvariant()) -replace '[^a-z0-9_.-]', '_'

        $meshAssets += [pscustomobject]@{
            id   = $id
            type = 'Model'
        }

        $hostAssets += [pscustomobject]@{
            id         = "$id.raylib"
            assetKind  = 'Mesh'
            assetId    = $id
            backendId  = 'raylib'
            sourceUris = @("KayKitKenneyAssetPackMod:assets/Models/$relativeUri")
        }
    }

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $meshAssetsJson = ConvertTo-JsonArray -Items $meshAssets -SortProperty 'id'
    $hostAssetsJson = ConvertTo-JsonArray -Items $hostAssets -SortProperty 'id'
    [System.IO.File]::WriteAllText(
        (Join-Path $presentationRoot 'mesh_assets.json'),
        $meshAssetsJson,
        $utf8NoBom)
    [System.IO.File]::WriteAllText(
        (Join-Path $presentationRoot 'host_assets.json'),
        $hostAssetsJson,
        $utf8NoBom)

    foreach ($name in 'animation_clips', 'animation_profiles', 'animator_controllers') {
        $path = Join-Path $presentationRoot "$name.json"
        if (-not (Test-Path -LiteralPath $path)) {
            [System.IO.File]::WriteAllText($path, '[]', $utf8NoBom)
        }
    }

    Write-Host "Regenerated Presentation mesh_assets.json and host_assets.json with $($modelFiles.Count) model(s)."
}

foreach ($pack in $packs) {
    Import-Pack -Pack $pack
}

Update-PresentationRegistries
Write-Host "KayKit/Kenney asset import complete."
