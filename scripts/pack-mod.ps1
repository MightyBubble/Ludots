# Mod 作者产出预编译（BinaryOnly）发布包（epic #1190 族E）。
# 玩家侧分发约定：包内只有 mod.json + bin/net9.0/*.dll + 资源，不含 .cs/.csproj，
# 玩家机加载 BinaryOnly mod 不触发编译，无需 .NET SDK。
# 用法（开发机）：
#   .\scripts\pack-mod.ps1 -ModDir mods\ExampleMod              # 输出到 dist\mods\ExampleMod
#   .\scripts\pack-mod.ps1 -ModDir mods\ExampleMod -OutDir zips # 输出到 zips\ExampleMod
param(
    [Parameter(Mandatory = $true)]
    [string]$ModDir,
    [string]$OutDir = "dist/mods",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = 'Stop'

# ModDir 可相对当前目录或仓库根
$candidates = @(
    [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $ModDir)),
    [System.IO.Path]::GetFullPath((Join-Path (Join-Path $PSScriptRoot "..") $ModDir))
)
$resolved = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $resolved) { throw "Mod directory not found: $ModDir (tried: $($candidates -join '; '))" }
$ModDir = $resolved

$modName = Split-Path -Leaf $ModDir
$manifestPath = Join-Path $ModDir 'mod.json'
if (-not (Test-Path $manifestPath)) { throw "mod.json not found in $ModDir" }
$project = Get-ChildItem $ModDir -Filter *.csproj | Select-Object -First 1

# 1) Release 构建
if ($project) {
    Write-Host "build: $($project.FullName)"
    dotnet build $project.FullName -c $Configuration -v quiet -nologo
    if ($LASTEXITCODE -ne 0) { throw "mod build failed" }
}

# 2) 校验 main 程序集
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$mainDll = Join-Path $ModDir $manifest.main
if (-not (Test-Path $mainDll)) { throw "main assembly missing after build: $($manifest.main) (expect $mainDll)" }

# 3) 复制为 BinaryOnly 包（防泄漏兜底检查）
$dst = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutDir))
$dst = Join-Path $dst $modName
if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dst | Out-Null
robocopy $ModDir $dst /E /XF *.cs *.csproj /XD obj Debug | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed" }
$leak = Get-ChildItem $dst -Recurse -Include *.cs, *.csproj | Select-Object -First 1
if ($leak) { throw "source leaked into package: $($leak.FullName)" }

Write-Host "packed: $dst (BinaryOnly, main=$($manifest.main))"
