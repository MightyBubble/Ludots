# 环境与构建

本页定义当前 Ludots 的正式环境要求、构建命令和启动入口。

## 1 SDK 要求

- .NET 9.0（`global.json` 固定 SDK 9.0.x，全仓 target `net9.0`）
- Node.js + npm

## 2 常用构建命令

```powershell
dotnet build src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj -c Release
dotnet build src/Tools/Ludots.Editor.Bridge/Ludots.Editor.Bridge.csproj -c Release
```

## 3 常用启动命令

```powershell
.\scripts\run-mod-launcher.cmd
.\scripts\run-mod-launcher.cmd cli resolve camera_acceptance --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch camera_acceptance --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch camera_acceptance --adapter web
.\scripts\run-authoring-studio.cmd
```

Linux / macOS 作者工作室：

```bash
./scripts/run-authoring-studio.sh
```

规则：

- 正式入口是 launcher web 和 launcher CLI。
- `.\scripts\run-mod-launcher.cmd cli ...` 是 canonical wrapper 形式。
- 直接运行 `src/Apps/...` 只用于 adapter 级调试。

## 4 常用测试命令

```powershell
dotnet test src/Tests/GasTests/GasTests.csproj
dotnet test src/Tests/ThreeCTests/ThreeCTests.csproj
dotnet test src/Tests/PresentationTests/PresentationTests.csproj
dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj
```

验收图、战报、基准 CSV 默认写到这次测试的工作目录（NUnit 的 `WorkDirectory`，一般在项目 `bin` 下）。仓库里已经提交的那一份保持不动。画廊和基准页读的是仓库里的文件：`showcase.registry.json` 引用的截图，`docs/benchmarks/` 的表，以及 `artifacts/acceptance/`、`artifacts/benchmarks/`、`artifacts/showcases/capability-standard-*` 下的战报。

要更新这些入库证据，在仓库根把 `LUDOTS_UPDATE_COMMITTED_TEST_ARTIFACTS` 设为 `1`，再跑原来的测试。开关只认 `1`。产物仍写到原来的仓库相对路径。跑完看 `git status`，提交要留下的文件。

```bash
LUDOTS_UPDATE_COMMITTED_TEST_ARTIFACTS=1 dotnet test src/Tests/GasTests/GasTests.csproj --filter "FullyQualifiedName~GraphBehaviorPressureMatrixTests"
```

```powershell
$env:LUDOTS_UPDATE_COMMITTED_TEST_ARTIFACTS = "1"
dotnet test src/Tests/GasTests/GasTests.csproj --filter "FullyQualifiedName~GraphBehaviorPressureMatrixTests"
```

两份基线另有自己的开关，决定这次写不写文件。文件进不进仓库，仍看 `LUDOTS_UPDATE_COMMITTED_TEST_ARTIFACTS`：

- 装载期容量：`LUDOTS_EMIT_CAPACITY_BASELINE=1`，路径 `docs/rfcs/gas-loadtime-capacity/benchmark-<phase>.json`
- 迷雾采样：`L76_OVERWRITE_BASELINE=1`，路径 `src/Tests/CrowdSimulationTests/Baselines/l76-fog.json`。这条测试标了 Explicit，过滤名 `WriteL76FogBaseline`

`solution-verify` 在测试跑完后执行 `git status --porcelain`。工作区有改动，这一步失败。CI 不设置上面的开关。图节点画廊生成器在测试之前单独跑，并自己核对生成结果没有漂移。

## 5 平台说明

- Linux / Raylib 需要本机原生依赖。
- Cloud VM 更适合 CLI、bridge 和 web adapter。
- Mod 不局限于仓库 `mods/` 目录，launcher 支持外部 scan roots、workspace 和 binding。

## 6 深度材料

- 仓库深度版：`docs/conventions/03_environment_setup.md`
- CLI 手册：`docs/reference/cli_runbook.md`
