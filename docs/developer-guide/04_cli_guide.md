# CLI 启动与调试指南

Ludots 提供了灵活的命令行接口 (CLI)，用于快速启动、测试和调试游戏配置。

## 1. 启动脚本

在 `scripts/` 目录下，提供了常用的启动脚本，封装了 `dotnet run` 命令。

### 1.1 Mod 启动器 (推荐)

使用 `scripts/run-mod-launcher.cmd` 启动可视化配置界面。

```bash
# 启动 Mod Launcher
.\scripts\run-mod-launcher.cmd

# 启动特定配置 (命令行参数)
.\scripts\run-mod-launcher.cmd -- --config assets/game.debug.json
```

### 1.2 直接启动 App (调试用)

使用 `scripts/run-app.cmd` 直接运行主程序（跳过 Launcher）。

```bash
# 默认配置
.\scripts\run-app.cmd

# 指定 game.json
.\scripts\run-app.cmd -- game.navigation2d.json
```

## 2. 命令行参数详解

### 2.1 应用程序 (App) 参数

主程序 `Ludots.App.Raylib.exe` 接受以下参数：

*   **config_path**: (位置参数 0) 指定 `game.json` 的相对路径。默认为 `game.json`。

### 2.2 ModLauncher 参数

ModLauncher (`src/Tools/ModLauncher`) 支持以下参数：

*   `--preset <id>`: 加载预设配置（如 `debug`, `release`）。
*   `--config <path>`: 指定配置文件路径。
*   `--mod <name>` / `--mods <list>`: 强制启用特定 Mod（覆盖默认配置）。
*   `--help`: 显示帮助信息。

## 3. 调试技巧

### 3.1 调试特定 Mod

如果你正在开发 `MyNewMod`，可以通过 CLI 快速启动仅包含该 Mod 的配置：

```bash
dotnet run --project src/Tools/ModLauncher/ModLauncher.csproj -- --mods "MyNewMod"
```

### 3.2 Visual Studio / Rider 调试配置

在 IDE 中配置启动参数：

*   **Project**: `Ludots.App.Raylib`
*   **Arguments**: `game.debug.json` (或你的测试配置)
*   **Working Directory**: `$(ProjectDir)/../../../../` (指向仓库根目录)

这允许你在 IDE 中直接 F5 调试，并加载正确的资源路径。
