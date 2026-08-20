[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\..\external\cc0-assets'),
    [ValidateSet('all', 'kenney', 'kaykit')]
    [string]$Provider = 'all'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-Page {
    param([Parameter(Mandatory)][string]$Uri)

    try {
        $content = & curl.exe --location --fail --silent --show-error --connect-timeout 30 --max-time 90 $Uri
        if ($LASTEXITCODE -ne 0) {
            throw "curl exited with code $LASTEXITCODE."
        }
        return ($content -join "`n")
    }
    catch {
        throw "Failed to retrieve required source page: $Uri. $($_.Exception.Message)"
    }
}

function Get-Sha256 {
    param([Parameter(Mandatory)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-ZipArchive {
    param([Parameter(Mandatory)][string]$Path)

    $archive = $null
    try {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
        if ($archive.Entries.Count -eq 0) {
            throw 'archive contains no entries.'
        }
        $archive.Dispose()
    }
    catch {
        if ($null -ne $archive) {
            $archive.Dispose()
        }
        throw "Invalid ZIP archive: $Path. $($_.Exception.Message)"
    }
}

function Download-Asset {
    param(
        [Parameter(Mandatory)][string]$Uri,
        [Parameter(Mandatory)][string]$Path
    )

    if (Test-Path -LiteralPath $Path) {
        return
    }

    $temporaryPath = "$Path.download"
    Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
    try {
        Write-Host "Downloading $([System.IO.Path]::GetFileName($Path))"
        & curl.exe --location --fail --silent --show-error --connect-timeout 30 --max-time 300 --output $temporaryPath $Uri
        if ($LASTEXITCODE -ne 0) {
            throw "curl exited with code $LASTEXITCODE."
        }
        Move-Item -LiteralPath $temporaryPath -Destination $Path
    }
    catch {
        Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
        throw "Failed to download required asset: $Uri. $($_.Exception.Message)"
    }
}

function Get-KenneyPacks {
    $assetUris = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $firstCatalog = Get-Page -Uri 'https://kenney.nl/assets'
    $pageMatches = [regex]::Matches($firstCatalog, 'assets/page:(\d+)')
    $pageCount = ($pageMatches | ForEach-Object { [int]$_.Groups[1].Value } | Measure-Object -Maximum).Maximum
    if ($null -eq $pageCount) {
        throw 'Kenney catalog did not declare its last page.'
    }

    $catalogs = @($firstCatalog)
    for ($page = 2; $page -le $pageCount; $page++) {
        $catalogs += Get-Page -Uri "https://kenney.nl/assets/page:$page"
    }

    foreach ($catalog in $catalogs) {
        foreach ($match in [regex]::Matches($catalog, 'href=[''"](https://kenney\.nl/assets/[^''"?#]+)[''"]')) {
            $uri = $match.Groups[1].Value
            if ($uri -notmatch '/(category|series|tag|page):') {
                [void]$assetUris.Add($uri)
            }
        }
    }

    if ($assetUris.Count -eq 0) {
        throw 'Kenney catalog yielded no asset pages.'
    }

    foreach ($assetUri in $assetUris | Sort-Object) {
        $assetPage = Get-Page -Uri $assetUri
        if ($assetPage -notmatch 'Creative Commons CC0') {
            throw "Kenney asset is not explicitly CC0: $assetUri"
        }

        $downloadMatch = [regex]::Match($assetPage, 'href=[''"](https://kenney\.nl/media/pages/assets/[^''"]+?\.zip)[''"]')
        if (-not $downloadMatch.Success) {
            throw "Kenney asset has no official ZIP download link: $assetUri"
        }

        [pscustomobject]@{
            Name = ([uri]$assetUri).Segments[-1].TrimEnd('/')
            Source = $assetUri
            License = 'https://creativecommons.org/publicdomain/zero/1.0/'
            Download = $downloadMatch.Groups[1].Value
        }
    }
}

function Get-KayKitPacks {
    $profileUri = 'https://github.com/KayKit-Game-Assets?tab=repositories'
    $profile = Get-Page -Uri $profileUri
    $repositoryNames = [regex]::Matches($profile, 'href="/KayKit-Game-Assets/(KayKit-[A-Za-z0-9.-]+)" itemprop="name codeRepository"') |
        ForEach-Object { $_.Groups[1].Value } |
        Sort-Object -Unique
    if ($repositoryNames.Count -eq 0) {
        throw 'KayKit public repository page yielded no asset repositories.'
    }

    foreach ($repositoryName in $repositoryNames) {
        $repositoryUri = "https://github.com/KayKit-Game-Assets/$repositoryName.git"
        $head = & git ls-remote --symref $repositoryUri HEAD
        if ($LASTEXITCODE -ne 0) {
            throw "Could not resolve default branch for KayKit repository: $repositoryUri"
        }
        $defaultBranchMatch = [regex]::Match(($head -join "`n"), 'ref: refs/heads/([^\s]+)\s+HEAD')
        if (-not $defaultBranchMatch.Success) {
            throw "KayKit repository did not expose a default branch: $repositoryUri"
        }

        $defaultBranch = $defaultBranchMatch.Groups[1].Value
        $readmeUri = "https://raw.githubusercontent.com/KayKit-Game-Assets/$repositoryName/$defaultBranch/README.md"
        $readme = Get-Page -Uri $readmeUri
        if ($readme -notmatch '(?i)CC0|Creative Commons Zero') {
            throw "KayKit repository is not explicitly CC0: $repositoryUri"
        }

        [pscustomobject]@{
            Name = $repositoryName
            Source = "https://github.com/KayKit-Game-Assets/$repositoryName"
            License = $readmeUri
            Download = "https://github.com/KayKit-Game-Assets/$repositoryName/archive/refs/heads/$defaultBranch.zip"
        }
    }
}

$resolvedDestination = [System.IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path $resolvedDestination -Force | Out-Null

$manifest = [System.Collections.Generic.List[object]]::new()
$providers = @()
if ($Provider -in @('all', 'kenney')) {
    $providers += @{ Name = 'kenney'; Packs = @(Get-KenneyPacks) }
}
if ($Provider -in @('all', 'kaykit')) {
    $providers += @{ Name = 'kaykit'; Packs = @(Get-KayKitPacks) }
}

foreach ($providerDefinition in $providers) {
    $providerDirectory = Join-Path $resolvedDestination $providerDefinition.Name
    New-Item -ItemType Directory -Path $providerDirectory -Force | Out-Null
    foreach ($pack in $providerDefinition.Packs) {
        $archivePath = Join-Path $providerDirectory "$($pack.Name).zip"
        Download-Asset -Uri $pack.Download -Path $archivePath
        $archive = Get-Item -LiteralPath $archivePath
        if ($archive.Length -eq 0) {
            throw "Downloaded archive is empty: $archivePath"
        }
        Assert-ZipArchive -Path $archivePath

        $manifest.Add([ordered]@{
                provider = $providerDefinition.Name
                name = $pack.Name
                source = $pack.Source
                license = $pack.License
                download = $pack.Download
                archive = [System.IO.Path]::GetRelativePath($resolvedDestination, $archivePath).Replace('\', '/')
                bytes = $archive.Length
                sha256 = Get-Sha256 -Path $archivePath
            })
    }
}

$manifestPath = Join-Path $resolvedDestination "manifest.$Provider.json"
[ordered]@{
    schema = 'ludots.cc0-asset-archive.v1'
    generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    packCount = $manifest.Count
    packs = $manifest
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8

Write-Host "Downloaded and verified $($manifest.Count) CC0 packs to $resolvedDestination"
