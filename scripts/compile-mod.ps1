# 无 SDK 的 mod 编译（epic #1190 族E）：内嵌 Roslyn 进程内编译，产出与 mods csproj 等价的
# bin/net9.0/*.dll。玩家/作者机不需要 .NET SDK（配合自包含分发的 Ludots.ModCompiler）。
# 用法：.\scripts\compile-mod.ps1 -ModDir mods\ExampleMod
param(
    [Parameter(Mandatory = $true)]
    [string]$ModDir
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

# ModSdk 引用集缺失时先导出（launcher build 的副产品；repo 用户有 SDK，一次性动作）
$modSdkRef = Join-Path $repoRoot 'assets/ModSdk/ref'
if (-not (Test-Path $modSdkRef)) {
    Write-Host "exporting ModSdk refs (one-time)..."
    $launcherDll = Join-Path $repoRoot 'src/Tools/Ludots.Launcher.Cli/bin/Release/net9.0/Ludots.Launcher.Cli.dll'
    if (-not (Test-Path $launcherDll)) {
        dotnet build (Join-Path $repoRoot 'src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj') -c Release -v quiet -nologo
        if ($LASTEXITCODE -ne 0) { throw "launcher build failed" }
    }
    dotnet $launcherDll build mod:ExampleMod --adapter raylib | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "ModSdk export failed" }
}

$compilerDll = Join-Path $repoRoot 'src/Tools/Ludots.ModCompiler/bin/Release/net9.0/Ludots.ModCompiler.dll'
if (-not (Test-Path $compilerDll)) {
    dotnet build (Join-Path $repoRoot 'src/Tools/Ludots.ModCompiler/Ludots.ModCompiler.csproj') -c Release -v quiet -nologo
    if ($LASTEXITCODE -ne 0) { throw "mod compiler build failed" }
}

dotnet $compilerDll $ModDir
exit $LASTEXITCODE
