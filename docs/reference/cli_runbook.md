# CLI 运行与 Launcher 手册

本文收口 Ludots 的启动脚本、CLI 入口和推荐 Mod 组合。当前 GUI 主路径是 `Ludots.Editor.Bridge` + `Ludots.Launcher.React`，不再把 WPF `ModLauncher` 作为新增能力的主承载面。

## 1 启动脚本

### 1.1 Web Launcher

推荐直接启动 Web launcher：

```bash
.\scripts\run-launcher.cmd
```

或使用 PowerShell：

```bash
.\scripts\run-launcher.ps1
```

这会启动：

- `src/Tools/Ludots.Editor.Bridge/Ludots.Editor.Bridge.csproj`
- `src/Tools/Ludots.Launcher.React`

默认地址：

- Bridge: `http://localhost:5299`
- Launcher: `http://localhost:5174`

停止：

```bash
.\scripts\stop-launcher.cmd
```

### 1.2 Editor + Bridge

地图编辑器仍走独立的 editor 前端：

```bash
.\scripts\run-editor.cmd
.\scripts\stop-editor.cmd
```

### 1.3 ModLauncher CLI

`ModLauncher` 保留 CLI 能力，用于构建、写入 `game.json` 和运行桌面 App：

```bash
.\scripts\run-mod-launcher.cmd -- cli <primary> <secondary> [options]
```

## 2 ModLauncher CLI 常用命令

```bash
# 导出 Mod SDK
dotnet run --project src/Tools/ModLauncher/Ludots.ModLauncher.csproj -c Release -- cli sdk export

# 构建 Raylib App
dotnet run --project src/Tools/ModLauncher/Ludots.ModLauncher.csproj -c Release -- cli app build

# 构建指定 Mod
dotnet run --project src/Tools/ModLauncher/Ludots.ModLauncher.csproj -c Release -- cli mods build --mods "MyModA;MyModB"

# 写入运行时 game.json
dotnet run --project src/Tools/ModLauncher/Ludots.ModLauncher.csproj -c Release -- cli gamejson write --mods "MyModA;MyModB"

# 运行 Raylib App
dotnet run --project src/Tools/ModLauncher/Ludots.ModLauncher.csproj -c Release -- cli run
```

## 3 CLI Options

- `--preset <id>`：使用预设的 Mod 组合。
- `--config <path>`：指定 launcher 配置文件。
- `--mod <name>`：追加单个 Mod。
- `--mods "<a;b;c>"`：一次传入多个 Mod，使用 `;` 分隔。

## 4 推荐 Mod 组合

- `CoreInputMod` + `CameraProfilesMod`
  用于共享输入 + 视角模式切换。
- `CoreInputMod` + `CameraProfilesMod` + `CameraBootstrapMod`
  用于地图默认视角 + 按地图边界自动归中。
- `CoreInputMod` + `CameraProfilesMod` + `VirtualCameraShotsMod`
  用于 declarative virtual camera shots。
- `CameraAcceptanceMod`
  最小验收夹具；会组合 `CameraProfilesMod`、`CameraBootstrapMod`、`VirtualCameraShotsMod`。
- `MobaDemoMod`
  完整 MOBA 示例；保留通用输入主线。

## 5 工作目录与调试

### 5.1 `game.json` 的职责

App 旁边的 `game.json` 只承担引导职责：

- 仅包含 `ModPaths`
- 不承载实际运行时配置
- 实际配置由 ConfigPipeline 从 Core + Mods 合并

### 5.2 IDE 调试

Raylib App 调试建议：

- Project: `Ludots.App.Raylib`
- Arguments: `game.json`
- Working Directory: 指向可执行文件输出目录

### 5.3 Web launcher 调试

推荐分两个终端：

```bash
dotnet run --project src/Tools/Ludots.Editor.Bridge/Ludots.Editor.Bridge.csproj
```

```bash
cd src/Tools/Ludots.Launcher.React
npm run dev
```

## 6 相关文档

- 环境与构建：见 `docs/conventions/03_environment_setup.md`
- 启动顺序与入口：见 `docs/architecture/startup_entrypoints.md`
- Mod 运行时唯一真相：见 `docs/architecture/mod_runtime_single_source_of_truth.md`
