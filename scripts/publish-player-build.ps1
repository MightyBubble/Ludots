# 产出玩家零安装发行包（epic #1190 族D+E）：
#   - 自包含 Raylib 应用 + 自包含 Launcher.Cli（玩家机无需安装 .NET / Node）
#   - mods 以 BinaryOnly 形态打包（剔除 .cs/.csproj/obj），配合 --build never 全链免编译
# 跨平台：默认按当前 OS 选择 RID（win-x64 / linux-x64 / osx-x64 / osx-arm64），可用 -RuntimeIdentifier 覆盖。
# 用法（开发机，需要 .NET 9 SDK；Windows PowerShell 或跨平台 pwsh）：
#   .\scripts\publish-player-build.ps1
#   .\scripts\publish-player-build.ps1 -Mods ExampleMod
#   .\scripts\publish-player-build.ps1 -RuntimeIdentifier linux-x64
param(
    [string]$OutputDir = "dist/player",
    [string]$RuntimeIdentifier = "",
    [string[]]$Mods = @(),
    [string]$DefaultSelector = "mod:LudotsCoreMod mod:ExampleMod",
    [switch]$SkipAssets
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) ".."))
$pkg = Join-Path $repoRoot $OutputDir
$tfm = 'net9.0'

function Resolve-DefaultRid {
    if ($IsWindows -or $env:OS -eq 'Windows_NT') { return 'win-x64' }
    if ($IsMacOS) {
        $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
        if ($arch -eq [System.Runtime.InteropServices.Architecture]::Arm64) { return 'osx-arm64' }
        return 'osx-x64'
    }
    return 'linux-x64'
}

function Copy-TreeFiltered {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination,
        [string[]]$ExcludeFileExtensions = @(),
        [string[]]$ExcludeDirectoryNames = @()
    )

    if (-not (Test-Path $Source)) { throw "copy source missing: $Source" }
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    $excludeExt = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($ext in $ExcludeFileExtensions) { [void]$excludeExt.Add($ext) }
    $excludeDir = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in $ExcludeDirectoryNames) { [void]$excludeDir.Add($name) }

    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        if ($_.PSIsContainer) {
            if ($excludeDir.Contains($_.Name)) { return }
            Copy-TreeFiltered -Source $_.FullName -Destination (Join-Path $Destination $_.Name) `
                -ExcludeFileExtensions $ExcludeFileExtensions -ExcludeDirectoryNames $ExcludeDirectoryNames
            return
        }

        if ($excludeExt.Contains($_.Extension)) { return }
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $Destination $_.Name) -Force
    }
}

if ([string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
    $RuntimeIdentifier = Resolve-DefaultRid
}

Write-Host "== Ludots player build =="
Write-Host "repo:   $repoRoot"
Write-Host "output: $pkg"
Write-Host "rid:    $RuntimeIdentifier"

# 1) 选取要打包的 mods（默认：非 fixtures 的全部 mod，含纯资源 mod）
$modProjects = Get-ChildItem -Path (Join-Path $repoRoot 'mods') -Recurse -Filter *.csproj |
    Where-Object { $_.FullName -notmatch 'mods[\\/]fixtures[\\/]' -and $_.FullName -notmatch '[\\/]obj[\\/]' }
$manifestDirs = Get-ChildItem -Path (Join-Path $repoRoot 'mods') -Recurse -Filter mod.json |
    Where-Object { $_.FullName -notmatch 'mods[\\/]fixtures[\\/]' } |
    ForEach-Object { Split-Path -Parent $_.FullName }
if ($Mods -and $Mods.Count -gt 0) {
    $byName = @{}
    foreach ($dir in $manifestDirs) {
        $m = Get-Content (Join-Path $dir 'mod.json') -Raw | ConvertFrom-Json
        $byName[[string]$m.name] = $dir
    }
    $queue = [System.Collections.Generic.Queue[string]]::new()
    foreach ($n in $Mods) { $queue.Enqueue($n) }
    $queue.Enqueue('LudotsCoreMod')
    $wanted = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    while ($queue.Count -gt 0) {
        $n = $queue.Dequeue()
        if (-not $wanted.Add($n)) { continue }
        if (-not $byName.ContainsKey($n)) { throw "mod not found for closure: $n" }
        $deps = (Get-Content (Join-Path $byName[$n] 'mod.json') -Raw | ConvertFrom-Json).dependencies
        if ($deps) { foreach ($p in $deps.PSObject.Properties.Name) { $queue.Enqueue($p) } }
    }
    $wantedDirs = @($wanted | ForEach-Object { $byName[$_] })
    $modProjects = @($modProjects | Where-Object {
        $dir = Split-Path -Parent $_.FullName
        $wantedDirs -contains $dir
    })
}
else {
    $wantedDirs = @($manifestDirs)
}

$resourceDirs = @($wantedDirs | Where-Object { -not (Get-ChildItem $_ -Filter *.csproj | Select-Object -First 1) })
if (-not $modProjects -or $modProjects.Count -eq 0) { throw "No mods selected for packaging." }

# 2) Release 构建
foreach ($project in $modProjects) {
    Write-Host "build mod: $($project.BaseName)"
    & dotnet build $project.FullName -c Release -v quiet -nologo
    if ($LASTEXITCODE -ne 0) { throw "mod build failed: $($project.FullName)" }
}

# 3) 组包
if (Test-Path $pkg) { Remove-Item $pkg -Recurse -Force }
New-Item -ItemType Directory -Force -Path $pkg | Out-Null

$pkgMods = Join-Path $pkg 'mods'
New-Item -ItemType Directory -Force -Path $pkgMods | Out-Null
foreach ($project in $modProjects) {
    $modDir = Split-Path -Parent $project.FullName
    $modName = $project.BaseName
    $dst = Join-Path $pkgMods $modName
    Copy-TreeFiltered -Source $modDir -Destination $dst `
        -ExcludeFileExtensions @('.cs', '.csproj') -ExcludeDirectoryNames @('obj', 'Debug')
    $mainDll = [System.IO.Path]::Combine($dst, 'bin', $tfm, "$modName.dll")
    if (-not (Test-Path $mainDll)) { throw "packaged mod missing main assembly: $mainDll" }
}

foreach ($dir in $resourceDirs) {
    $dst = Join-Path $pkgMods (Split-Path -Leaf $dir)
    Copy-TreeFiltered -Source $dir -Destination $dst `
        -ExcludeFileExtensions @('.cs', '.csproj') -ExcludeDirectoryNames @('obj', 'bin')
}

$appOut = [System.IO.Path]::Combine($pkg, 'src', 'Apps', 'Raylib', 'Ludots.App.Raylib', 'bin', 'Release', $tfm)
& dotnet publish (Join-Path $repoRoot 'src/Apps/Raylib/Ludots.App.Raylib/Ludots.App.Raylib.csproj') `
    -c Release -r $RuntimeIdentifier --self-contained true -o $appOut -v quiet -nologo
if ($LASTEXITCODE -ne 0) { throw "app publish failed" }

$launcherOut = Join-Path $pkg 'tools/launcher'
& dotnet publish (Join-Path $repoRoot 'src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj') `
    -c Release -r $RuntimeIdentifier --self-contained true -o $launcherOut -v quiet -nologo
if ($LASTEXITCODE -ne 0) { throw "launcher publish failed" }

if (-not $SkipAssets) {
    Copy-TreeFiltered -Source (Join-Path $repoRoot 'assets') -Destination (Join-Path $pkg 'assets') `
        -ExcludeDirectoryNames @('bin', 'obj')
}
Copy-Item (Join-Path $repoRoot 'launcher.config.json') $pkg
$presetsPath = Join-Path $repoRoot 'launcher.presets.json'
if (Test-Path $presetsPath) { Copy-Item $presetsPath $pkg } else { Write-Host "note: launcher.presets.json absent, skipped" }

$isWindowsRid = $RuntimeIdentifier.StartsWith('win-', [StringComparison]::OrdinalIgnoreCase)
$launcherName = if ($isWindowsRid) { 'Ludots.Launcher.Cli.exe' } else { 'Ludots.Launcher.Cli' }
$launcherRelUnix = "tools/launcher/$launcherName"

if ($isWindowsRid) {
    $playCmd = @(
        '@echo off',
        'rem Ludots player entry - no dotnet/node install required',
        ("`"%~dp0tools\launcher\{0}`" launch --adapter raylib --build never {1} %*" -f $launcherName, $DefaultSelector)
    )
    [System.IO.File]::WriteAllLines((Join-Path $pkg 'Play.cmd'), $playCmd)
}
else {
    $playPath = Join-Path $pkg 'Play.sh'
    $nl = [Environment]::NewLine
    $playBody = '#!/usr/bin/env bash' + $nl +
        'set -euo pipefail' + $nl +
        'ROOT="$(cd "$(dirname "$0")" && pwd)"' + $nl +
        ('exec "$ROOT/{0}" launch --adapter raylib --build never {1} "$@"' -f $launcherRelUnix, $DefaultSelector) + $nl
    [System.IO.File]::WriteAllText($playPath, $playBody)
    if (-not ($IsWindows -or $env:OS -eq 'Windows_NT')) {
        & chmod +x $playPath
        $launcherHost = Join-Path $launcherOut $launcherName
        if (Test-Path $launcherHost) { & chmod +x $launcherHost }
        $appHost = Join-Path $appOut 'Ludots.App.Raylib'
        if (Test-Path $appHost) { & chmod +x $appHost }
    }
}

$entryHint = if ($isWindowsRid) { 'Play.cmd' } else { './Play.sh' }
$readmeLines = @(
    '# Ludots Player Build',
    '',
    "- 入口：$entryHint（默认 $DefaultSelector）。",
    "- 进阶：./$launcherRelUnix launch --adapter raylib --build never <selectors...>",
    "- RID=$RuntimeIdentifier；自包含运行时；mods 为 BinaryOnly，启动不编译、不访问 nuget.org。"
)
Set-Content -Path (Join-Path $pkg 'README-PLAYER.md') -Value $readmeLines -Encoding UTF8

$size = [math]::Round(((Get-ChildItem $pkg -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "done: $pkg ($size MB), mods=$($modProjects.Count), rid=$RuntimeIdentifier"
