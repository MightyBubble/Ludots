# 产出玩家零安装发行包（epic #1190 族D+E）：
#   - 自包含 Raylib 应用 + 自包含 Launcher.Cli（玩家机无需安装 .NET / Node）
#   - mods 以 BinaryOnly 形态打包（剔除 .cs/.csproj/obj），配合 --build never 全链免编译
# 用法（开发机，需要 .NET 9 SDK）：
#   .\scripts\publish-player-build.ps1                       # 全量 mods
#   .\scripts\publish-player-build.ps1 -Mods ExampleMod      # 只打包指定 mods
param(
    [string]$OutputDir = "dist/player",
    [string]$RuntimeIdentifier = "win-x64",
    [string[]]$Mods = @(),
    [string]$DefaultSelector = "mod:LudotsCoreMod mod:ExampleMod",
    [switch]$SkipAssets
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) ".."))
$pkg = Join-Path $repoRoot $OutputDir
$tfm = 'net9.0'

Write-Host "== Ludots player build =="
Write-Host "repo:   $repoRoot"
Write-Host "output: $pkg"

# 1) 选取要打包的 mods（默认：非 fixtures 的全部源码 mod）
$modProjects = Get-ChildItem -Path (Join-Path $repoRoot 'mods') -Recurse -Filter *.csproj |
    Where-Object { $_.FullName -notmatch 'mods[\\/]fixtures[\\/]' -and $_.FullName -notmatch '[\\/]obj[\\/]' }
if ($Mods -and $Mods.Count -gt 0) {
    $wanted = @($Mods | ForEach-Object { $_.ToLowerInvariant() })
    # LudotsCoreMod 携带 presentation/startup 的 game.json 基座，任何玩家包都需要它
    if ($wanted -notcontains 'ludotscoremod') { $wanted += 'ludotscoremod' }
    $modProjects = @($modProjects | Where-Object {
        $name = [System.IO.Path]::GetFileNameWithoutExtension($_.FullName).ToLowerInvariant()
        $wanted -contains $name
    })
}
if (-not $modProjects -or $modProjects.Count -eq 0) { throw "No mods selected for packaging." }

# 2) Release 构建（开发机完成编译；玩家包内不再有源码与编译步骤）
foreach ($project in $modProjects) {
    Write-Host "build mod: $($project.BaseName)"
    dotnet build $project.FullName -c Release -v quiet -nologo
    if ($LASTEXITCODE -ne 0) { throw "mod build failed: $($project.FullName)" }
}

# 3) 组包
if (Test-Path $pkg) { Remove-Item $pkg -Recurse -Force }
New-Item -ItemType Directory -Force -Path $pkg | Out-Null

# 3a) mods → BinaryOnly（剔除源码/工程文件/obj；保留 mod.json、bin/net9.0、资源目录）
$pkgMods = Join-Path $pkg 'mods'
New-Item -ItemType Directory -Force -Path $pkgMods | Out-Null
foreach ($project in $modProjects) {
    $modDir = Split-Path -Parent $project.FullName
    $modName = $project.BaseName
    $dst = Join-Path $pkgMods $modName
    robocopy $modDir $dst /E /XF *.cs *.csproj /XD obj Debug | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed for mod $modName" }
    $mainDll = [System.IO.Path]::Combine($dst, 'bin', $tfm, "$modName.dll")
    if (-not (Test-Path $mainDll)) { throw "packaged mod missing main assembly: $mainDll" }
}

# 3b) Raylib 应用自包含发布（放到 launcher 期望的仓库相对路径；apphost exe 由启动器优先直启）
$appOut = [System.IO.Path]::Combine($pkg, 'src', 'Apps', 'Raylib', 'Ludots.App.Raylib', 'bin', 'Release', $tfm)
dotnet publish (Join-Path $repoRoot 'src/Apps/Raylib/Ludots.App.Raylib/Ludots.App.Raylib.csproj') `
    -c Release -r $RuntimeIdentifier --self-contained true -o $appOut -v quiet -nologo
if ($LASTEXITCODE -ne 0) { throw "app publish failed" }

# 3c) Launcher.Cli 自包含发布（FindRepoRoot 向上找 assets/，包根含 assets 即可定位）
$launcherOut = Join-Path $pkg 'tools/launcher'
dotnet publish (Join-Path $repoRoot 'src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj') `
    -c Release -r $RuntimeIdentifier --self-contained true -o $launcherOut -v quiet -nologo
if ($LASTEXITCODE -ne 0) { throw "launcher publish failed" }

# 3d) assets / 配置
if (-not $SkipAssets) {
    robocopy (Join-Path $repoRoot 'assets') (Join-Path $pkg 'assets') /E | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed for assets" }
}
Copy-Item (Join-Path $repoRoot 'launcher.config.json') $pkg
Copy-Item (Join-Path $repoRoot 'launcher.presets.json') $pkg -ErrorAction SilentlyContinue

# 3e) 玩家入口
$playLines = @(
    '@echo off',
    'rem Ludots player entry - no dotnet/node install required',
    ('"%~dp0tools\launcher\Ludots.Launcher.Cli.exe" launch --adapter raylib --build never ' + $DefaultSelector + ' %*')
)
[System.IO.File]::WriteAllLines((Join-Path $pkg 'Play.cmd'), $playLines)

$readmeLines = @(
    '# Ludots Player Build',
    '',
    "- 双击 Play.cmd 启动（默认入口 $DefaultSelector）。",
    '- 进阶：tools\launcher\Ludots.Launcher.Cli.exe launch --adapter raylib --build never <selectors...>',
    '- 本包自带 .NET 运行时（self-contained）；mods 为预编译 BinaryOnly，启动不编译。'
)
Set-Content -Path (Join-Path $pkg 'README-PLAYER.md') -Value $readmeLines -Encoding UTF8

$size = [math]::Round(((Get-ChildItem $pkg -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "done: $pkg ($size MB), mods=$($modProjects.Count)"
