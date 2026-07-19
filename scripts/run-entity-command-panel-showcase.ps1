param(
    [string]$Configuration = 'Release',
    [switch]$NoBuild
)

# 推荐使用新增 preset（canonical 入口，经 launcher 统一解析 binding/构建依赖）：
#   scripts/run-mod-launcher.cmd cli launch --preset entity_command_panel_raylib
#   等价 selector 形式（见 gitbook/reference/cli-runbook.md）：
#   scripts/run-mod-launcher.cmd cli launch 'preset:entity_command_panel_raylib'
# 本脚本为历史遗留的手动路径：绕过 launcher 直接逐个 dotnet build 4 个 mod 并启动
# Raylib 宿主，仅用于需要精细控制构建顺序/参数的场景；常规验收请走上面的 preset。

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..'
$appProject = Join-Path $repoRoot 'src\Apps\Raylib\Ludots.App.Raylib\Ludots.App.Raylib.csproj'
$appDir = Split-Path -Parent $appProject
$modsToBuild = @(
    (Join-Path $repoRoot 'mods\EntityCommandPanelMod\EntityCommandPanelMod.csproj'),
    (Join-Path $repoRoot 'mods\capabilities\entityinfo\EntityInfoPanelsMod\EntityInfoPanelsMod.csproj'),
    (Join-Path $repoRoot 'mods\showcases\interaction\InteractionShowcaseMod\InteractionShowcaseMod.csproj'),
    (Join-Path $repoRoot 'mods\showcases\entity_command_panel\EntityCommandPanelShowcaseMod\EntityCommandPanelShowcaseMod.csproj')
)

if (-not (Test-Path $appProject)) {
    throw "Raylib app project not found: $appProject"
}

foreach ($modProject in $modsToBuild) {
    if (-not (Test-Path $modProject)) {
        throw "Mod project not found: $modProject"
    }
}

if (-not $NoBuild) {
    foreach ($modProject in $modsToBuild) {
        & dotnet build $modProject -c $Configuration -nologo
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }

    & dotnet build $appProject -c $Configuration -nologo
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

$arguments = @(
    'run',
    '--project', $appProject,
    '-c', $Configuration
)

if ($NoBuild) {
    $arguments += '--no-build'
}

$arguments += '--'
$arguments += 'launcher.entity-command-panel-showcase.runtime.json'

Push-Location $appDir
try {
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}

# 等价 canonical 命令（launcher preset 入口）：
#   scripts/run-mod-launcher.cmd cli launch --preset entity_command_panel_raylib
#   等价 selector 形式（见 gitbook/reference/cli-runbook.md）：
#   scripts/run-mod-launcher.cmd cli launch 'preset:entity_command_panel_raylib'
# preset 定义见 launcher.presets.json（selectors: $entity_command_panel_showcase），
# binding 定义见 launcher.config.json（entity_command_panel_showcase）。
