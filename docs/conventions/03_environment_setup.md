# 开发环境与构建

本篇汇总 Ludots 仓库的 SDK 要求、构建命令、测试命令、服务启动方式和平台特定说明。

## 1 SDK 要求

| SDK | 必需 | 原因 |
|-----|------|------|
| .NET 8.0 | 是 | 主目标框架 |
| .NET 9.0 | 是 | DotRecast 多目标编译 |
| .NET 10.0 (preview) | 是 | DotRecast 多目标编译 |
| Node.js + npm | 仅 Editor | Editor React 前端 |

缺少任一 .NET SDK 会导致 `dotnet restore` 失败。

### 1.1 Linux / Cloud VM 安装

SDKs 通过 `dotnet-install.sh` 安装到 `/usr/share/dotnet`，符号链接 `/usr/local/bin/dotnet`。`PATH` 和 `DOTNET_ROOT` 在 `~/.bashrc` 中设置。

## 2 构建命令

```bash
# 构建 Raylib 桌面应用
dotnet build src/Apps/Raylib/Ludots.App.Raylib/Ludots.App.Raylib.csproj -c Release

# 构建指定 Mod
.\scripts\run-mod-launcher.cmd cli mods build --mods "MobaDemoMod"

# 写入 game.json
.\scripts\run-mod-launcher.cmd cli gamejson write --mods "MobaDemoMod"

# 运行游戏
.\scripts\run-mod-launcher.cmd cli run
```

约束：

- `run-mod-launcher.cmd` 的规范参数形式是 `cli ...`，不要额外写 `-- cli ...`。
- 如果要启动一个具体 Mod，必须先执行 `cli gamejson write --mods ...`，因为 `cli run` 只读取 Raylib 输出目录旁边的 `game.json`。
- 推荐顺序是 `cli mods build -> cli app build -> cli gamejson write -> cli run`。

## 3 测试命令

```bash
# GAS 测试（核心 gameplay）
dotnet test src/Tests/GasTests/GasTests.csproj

# GAS 测试（详细输出）
dotnet test src/Tests/GasTests/GasTests.csproj --logger "console;verbosity=detailed"

# 运行指定测试类
dotnet test src/Tests/GasTests/GasTests.csproj --filter "FullyQualifiedName~TagRuleSetTests"

# 2D 导航测试
dotnet test src/Tests/Navigation2DTests/Navigation2DTests.csproj

# 架构边界测试
dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj
```

测试框架：NUnit 4.2.2 + BenchmarkDotNet。测试风格见 `src/Tests/GasTests/TESTING_STYLE.md`。

## 4 服务启动

Ludots 当前的 GUI 主路径是 Bridge + React。WPF `ModLauncher` 仍保留 CLI，但不再作为新增能力的主承载面。

### 4.1 Editor Bridge（ASP.NET Core API，端口 5299）

```bash
dotnet run --project src/Tools/Ludots.Editor.Bridge/Ludots.Editor.Bridge.csproj
```

提供 Mod/Map/Terrain 数据的 REST API，供 launcher 和 editor 共用。

### 4.2 Launcher React（Vite 前端，端口 5174）

```bash
cd src/Tools/Ludots.Launcher.React && npm run dev
```

或直接使用脚本：

```bash
.\scripts\run-launcher.cmd
```

### 4.3 Editor React（Vite 前端，端口 5173）

```bash
cd src/Tools/Ludots.Editor.React && npx vite --host 0.0.0.0 --port 5173
```

可视化地图编辑器，连接 Editor Bridge。

### 4.4 Raylib 桌面应用

```bash
dotnet run --project src/Apps/Raylib/Ludots.App.Raylib/Ludots.App.Raylib.csproj -c Release -- game.navigation2d.json
```

## 5 平台特定说明

### 5.1 Linux / Raylib

*   `libraylib.so`（raylib 5.5, x64）已签入 `src/Platforms/Desktop/`
*   `SkiaSharp.NativeAssets.Linux` NuGet 包提供 `libSkiaSharp.so`
*   csproj 使用 OS 条件 `<ItemGroup>` 按平台复制正确的原生库
*   系统依赖：`libx11-dev`, `libxrandr-dev`, `libxi-dev`, `libxcursor-dev`, `libxinerama-dev`, `libgl1-mesa-dev`

### 5.2 Cloud VM 限制

*   Raylib 桌面应用需要 GPU 和原生库，在无 GPU 的 Cloud VM 上可能无法运行
*   测试和 Editor 技术栈（Bridge + React）在 Cloud VM 上完全可用

### 5.3 已知问题

*   `src/Tools/Ludots.Editor.React/src/App.tsx` 存在大小写敏感的 import（`@/Components/...` vs `@/components/...`），Linux 上可能导致 `tsc` 和 Vite 失败
*   ESLint 报告约 71 个预存错误（`@typescript-eslint/no-explicit-any` 和 `no-case-declarations`），这些是历史问题

## 6 Mod 目录结构

Mod 位于仓库根目录的 `mods/`，不在 `src/` 内。这与 UGC 分发布局一致。

*   `modworkspace.json`（仓库根目录）列出要扫描 Mod 的目录
*   `game.json` 中的 `ModPaths` 条目指向包含 `mod.json` 的 Mod 目录
*   Mod 发现通过 workspace 配置或显式 `ModPaths`，不使用硬编码路径

## 7 相关文档

*   编码标准：见 [00_coding_standards.md](00_coding_standards.md)
*   CLI 启动与调试指南：见 `docs/reference/cli_runbook.md`
*   Mod 架构与配置系统：见 `docs/architecture/mod_architecture.md`
*   启动顺序与入口点：见 `docs/architecture/startup_entrypoints.md`

