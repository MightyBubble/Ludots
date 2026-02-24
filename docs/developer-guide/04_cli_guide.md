# CLI 启动与调试指南

Ludots 提供了灵活的命令行接口 (CLI)，用于快速启动、测试和调试游戏配置。

## 1 启动脚本

在 `scripts/` 目录下，提供了常用的启动脚本，封装了 `dotnet run` 命令。

### 1.1 Mod 启动器

使用 `scripts/run-mod-launcher.cmd` 启动可视化配置界面。

```bash
# 启动 Mod Launcher
.\scripts\run-mod-launcher.cmd

# 传递参数给 ModLauncher（注意：cmd 脚本需要用 -- 分隔）
.\scripts\run-mod-launcher.cmd -- cli run
```

### 1.2 编辑器与 Bridge

使用 `scripts/run-editor.cmd` 启动编辑器相关进程，使用 `scripts/stop-editor.cmd` 结束。

```bash
# 启动编辑器
.\scripts\run-editor.cmd

# 停止编辑器
.\scripts\stop-editor.cmd
```

## 2 ModLauncher CLI 常用命令

ModLauncher 的 CLI 采用二级命令风格。入口示例：

```bash
dotnet run --project src/Tools/ModLauncher/Ludots.ModLauncher.csproj -c Release -- cli <primary> <secondary> [options]
```

### 2.1 常用命令

```bash
# 导出 Mod SDK（生成/刷新 assets/ModSdk）
dotnet run --project src/Tools/ModLauncher/Ludots.ModLauncher.csproj -c Release -- cli sdk export

# 构建 Raylib App（Release）
dotnet run --project src/Tools/ModLauncher/Ludots.ModLauncher.csproj -c Release -- cli app build

# 构建指定 Mod（Release）
dotnet run --project src/Tools/ModLauncher/Ludots.ModLauncher.csproj -c Release -- cli mods build --mods "MyModA;MyModB"

# 写入运行时 game.json（只包含 ModPaths）
dotnet run --project src/Tools/ModLauncher/Ludots.ModLauncher.csproj -c Release -- cli gamejson write --mods "MyModA;MyModB"

# 运行 Raylib App（需要先 build + gamejson write）
dotnet run --project src/Tools/ModLauncher/Ludots.ModLauncher.csproj -c Release -- cli run
```

### 2.2 Options

*   `--preset <id>`：选择预设（如果没有 `--mods`，会从预设里取激活 Mod 列表）。
*   `--config <path>`：指定启动器配置文件路径。
*   `--mod <name>`：追加一个 Mod（可重复）。
*   `--mods "<a;b;c>"`：一次传多个 Mod，分隔符是分号 `;`。

## 3 调试与工作目录

### 3.1 工作目录约定

游戏进程在启动时会从“可执行文件旁边”的 `game.json` 读取 `ModPaths`（仅用于引导），然后通过 ConfigPipeline 合并所有实际配置。运行前建议先用 `cli gamejson write` 写好 game.json。

### 3.2 Visual Studio / Rider 调试配置

在 IDE 中配置启动参数：

*   **Project**: `Ludots.App.Raylib`
*   **Arguments**: `game.json`（通常放在输出目录，且由启动器生成）
*   **Working Directory**: 指向 exe 所在目录（确保能找到 `game.json`）

这允许你在 IDE 中直接 F5 调试，并加载正确的资源路径。
