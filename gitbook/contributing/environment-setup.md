# 环境与构建

本页定义当前 Ludots 的正式环境要求、构建命令和启动入口。新人分流见门户 [上手](https://mightybubble.github.io/Ludots/getting-started.html)；弱网包契约见 [零环境/零网络契约](../reference/zero-env-setup.md)。

## 1 SDK 要求

- .NET 9.0（`global.json` 固定 SDK 9.0.x，全仓 target `net9.0`）——唯一硬前置
- Node.js + npm：**仅** GUI 启动器 / Web adapter 需要；Raylib CLI / `dev-up` 主路径不需要
- 仓库自带离线 NuGet（`external/nuget/`）：规范路径弱网可用

## 2 常用构建命令

```powershell
dotnet build src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj -c Release
dotnet build src/Tools/Ludots.Editor.Bridge/Ludots.Editor.Bridge.csproj -c Release
```

弱网一键（推荐）：

```bash
./scripts/dev-up.sh          # Linux / macOS
.\scripts\dev-up.ps1         # Windows
```

## 3 常用启动命令

```powershell
.\scripts\run-mod-launcher.cmd
.\scripts\run-mod-launcher.cmd cli resolve camera_acceptance --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch camera_acceptance --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch camera_acceptance --adapter web
```

规则：

- 作者主路径优先 `scripts/dev-up.*`（离线包 + Raylib）。
- 正式入口还包括 launcher web 和 launcher CLI。
- `.\scripts\run-mod-launcher.cmd cli ...` 是 canonical wrapper 形式。
- 直接运行 `src/Apps/...` 只用于 adapter 级调试。

## 4 常用测试命令

```powershell
dotnet test src/Tests/GasTests/GasTests.csproj
dotnet test src/Tests/ThreeCTests/ThreeCTests.csproj
dotnet test src/Tests/PresentationTests/PresentationTests.csproj
dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj
```

## 5 平台说明

- Linux / Raylib 需要本机原生依赖。
- Cloud VM 更适合 CLI、bridge 和 web adapter。
- Mod 不局限于仓库 `mods/` 目录，launcher 支持外部 scan roots、workspace 和 binding。

## 6 深度材料

- 仓库深度版：`docs/conventions/03_environment_setup.md`
- CLI 手册：`docs/reference/cli_runbook.md`
- 玩家发行链：`gitbook/reference/player-build.md`
