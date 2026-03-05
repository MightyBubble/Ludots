# Godot 适配层

本篇描述 Ludots 的 Godot 4 适配层结构、与 Raylib 的差异、输入与渲染映射。

## 1 概述

Godot 适配层使 Ludots Core 能在 Godot 4 引擎中运行，复用现有 Mod、ConfigPipeline、GAS 等能力。采用「Godot 为主」模式：Godot 提供窗口、输入、渲染、主循环；Ludots Core 提供 ECS、GAS、Mod、ConfigPipeline。

## 2 目录结构

| 路径 | 职责 |
|------|------|
| `src/Apps/Godot/Ludots.App.Godot/` | Godot 项目根，Main.tscn、Main.cs |
| `src/Adapters/Godot/Ludots.Adapter.Godot/` | HostComposer、GodotHostLoop、GodotHostContext |
| `src/Client/Ludots.Client.Godot/` | GodotInputBackend、GodotViewController、GodotCameraAdapter、GodotScreenRayProvider、GodotPrimitiveRenderer、GodotDebugDrawRenderer、GodotScreenHudDrawer |

## 3 与 Raylib 的对应关系

| 层级 | Raylib | Godot |
|------|--------|-------|
| App 入口 | exe 可执行文件 | Godot 项目（Main.tscn） |
| Host | RaylibHostLoop.Run(setup) | GodotHostLoop.Initialize() + Tick(dt) |
| ScreenProjector | CoreScreenProjector | CoreScreenProjector（复用） |
| ViewController | RaylibViewController | GodotViewController |
| ScreenRayProvider | RaylibScreenRayProvider | GodotScreenRayProvider |

## 4 启动流程

1. Godot 主场景 `Main` 的 `_Ready()` 创建 GodotInputBackend（Node）、GodotViewController、GodotCameraAdapter、GodotScreenRayProvider
2. 调用 `GodotHostComposer.Compose(baseDir, "game.json", inputBackend)` 得到 `GodotHostSetup`
3. 创建 `GodotHostContext(viewController, cameraAdapter, screenRayProvider)`
4. 创建 `GodotHostLoop(setup, context)`，调用 `Initialize()`（engine.Start、LoadMap）
5. 每帧 `_Process(delta)` 调用 `hostLoop.Tick((float)delta)`

## 5 输入映射

`GodotInputBackend` 实现 `IInputBackend`，将 Ludots 的 `devicePath`（如 `<Keyboard>/W`、`<Mouse>/LeftButton`）映射到 Godot 的 `Key`、`MouseButton`。`GodotInputPathParser` 负责解析。鼠标滚轮通过 `_Input` 累积。

## 6 渲染

| 能力 | Raylib | Godot |
|------|--------|-------|
| PrimitiveDrawBuffer | RaylibPrimitiveRenderer | GodotPrimitiveRenderer（MeshInstance3D + BoxMesh/SphereMesh，对象池） |
| DebugDrawCommandBuffer | RaylibDebugDrawRenderer | GodotDebugDrawRenderer（ImmediateMesh 线框） |
| ScreenHudBatchBuffer | RaylibHostLoop.DrawScreenHud | GodotScreenHudDrawer（CanvasLayer + Control._Draw） |

Main 在 `_Process` 中于 Tick 之后调用 `GodotPrimitiveRenderer.Draw` 与 `GodotDebugDrawRenderer.Draw`；HUD 由 GodotScreenHudDrawer 在 `_Draw` 中绘制。

## 7 CLI 命令

```bash
# 构建 Godot 项目
dotnet run --project src/Tools/ModLauncher/Ludots.ModLauncher.csproj -c Release -- cli app build --platform godot

# 写入 game.json 到 Godot 项目
dotnet run --project src/Tools/ModLauncher/Ludots.ModLauncher.csproj -c Release -- cli gamejson write --platform godot --mods "MobaDemoMod"

# 运行 Godot（需设置 GODOT_PATH 环境变量或配置 GodotExecutablePath）
dotnet run --project src/Tools/ModLauncher/Ludots.ModLauncher.csproj -c Release -- cli run --platform godot
```

## 8 相关文档

*   [03 适配器原则与平台抽象](03_adapter_pattern.md)
*   [04 CLI 启动与调试指南](04_cli_guide.md)
*   [09 启动顺序与入口点](09_startup_entrypoints.md)
