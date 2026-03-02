# 启动顺序与入口点

本篇说明 Ludots 从进程启动到进入第一张地图的完整顺序：入口点在哪里、`game.json` 在启动阶段承担什么职责、引擎初始化时会做哪些关键步骤。

## 1 入口点一览

### 1.1 Raylib App

Raylib App 的入口在 `src/Apps/Raylib/Ludots.App.Raylib/Program.cs`。

职责：

*   解析命令行参数：首个参数作为 `game.json` 路径；未提供则默认使用 `game.json`。
*   创建平台 Host（RaylibGameHost）并进入运行循环。

### 1.2 Raylib Host 与 Compose

Raylib Host 在 `src/Adapters/Raylib/Ludots.Adapter.Raylib`。

职责：

*   Compose：把引擎、配置、输入、UI 等依赖组装好并注入 `engine.GlobalContext`。
*   Loop：每帧驱动 `engine.Tick(dt)`，并在启动阶段调用 `engine.Start()` 与 `engine.LoadMap(startupMapId)`。

### 1.3 ModLauncher CLI

ModLauncher 既可以以 GUI 方式运行，也可以以 `cli` 模式运行。

*   入口：`src/Tools/ModLauncher/App.xaml.cs`（首参为 `cli` 时进入 CLI）
*   命令分发：`src/Tools/ModLauncher/Cli/CliRunner.cs`

常用命令包括 `cli gamejson write`、`cli app build`、`cli mods build`、`cli run`。

## 2 app/game.json 的职责边界

App 旁边的 `game.json` 在启动流程里只承担"引导"职责：

*   仅包含 `ModPaths`，用于告诉引擎要加载哪些 Mod 目录。
*   不承载实际运行配置。实际运行配置来自 ConfigPipeline 合并（Core + Mods）。

相关实现：`src/Core/Hosting/GameBootstrapper.cs`。

## 3 引擎初始化顺序

以 `GameBootstrapper.InitializeFromBaseDirectory` 为主线：

1.  找到 `assets` 根目录（从 baseDirectory 往上寻找，直到发现 `assets/`）。
2.  读取 app/game.json，仅解析 `ModPaths` 并校验每个目录包含 `mod.json`。
3.  创建 `GameEngine` 并调用 `InitializeWithConfigPipeline(modPaths, assetsRoot)`。

在 `InitializeWithConfigPipeline` 内部，关键步骤是：

1.  初始化 VFS 并挂载 Core：`VFS.Mount("Core", assetsRoot)`。
2.  初始化基础设施：FunctionRegistry、TriggerManager、SystemFactoryRegistry、TriggerDecoratorRegistry、ModLoader、MapManager。
3.  加载 Mods（按依赖顺序）：`ModLoader.LoadMods(modPaths)`。
    *   每个 Mod 的 `OnLoad(IModContext)` 通过 `OnEvent()`、`SystemFactoryRegistry`、`TriggerDecorators` 等 API 注册扩展。
4.  创建 ConfigPipeline，并合并得到最终 GameConfig：`MergedConfig = ConfigPipeline.MergeGameConfig()`。
5.  初始化 ECS World、空间服务与 GameSession。
6.  初始化核心系统组与表现系统组，并把关键服务写入 `GlobalContext`。
7.  注册必要的内建 Trigger（例如配置热重载）。

## 4 从启动到进入第一张地图

RaylibHostLoop 的启动顺序是：

1.  `engine.Start()`：触发 `GameStart` 事件。
    *   EventHandler 按注册顺序执行（Mod 的 GameStart 回调：能力注册、团队关系等）
    *   全局 Trigger 按 Priority 升序执行
2.  `engine.LoadMap(config.StartupMapId)`：添加式加载 StartupMapId 指定的地图。
    *   创建 MapSession + Boards
    *   加载地形/导航/实体
    *   实例化 Map Trigger → 应用 TriggerDecorators → 注册
    *   `FireMapEvent(mapId, MapLoaded, ctx)` — EventHandler + Map-scoped Trigger 执行
3.  进入主循环：每帧调用 `engine.Tick(platformDeltaTime)`。

其中 `engine.Tick` 会先推进模拟（由 Pacemaker 决定是否推进 FixedStep），再推进表现循环（每帧都会执行）。

## 5 完整执行时序

```
=== APP STARTUP ===
1. ModLoader.LoadMods (拓扑排序)
   FOR EACH mod:
     mod.OnLoad(context):
       ├─ context.OnEvent(GameStart, handler)       ← 能力/团队注册
       ├─ context.OnEvent(MapLoaded, handler)        ← Map 事件回调
       ├─ context.SystemFactoryRegistry.Register()   ← System 工厂
       ├─ context.TriggerDecorators.Register()       ← Trigger 修饰
       └─ context.FunctionRegistry / VFS / ...       ← 其他扩展

=== GAME START ===
2. GameEngine.Start()
   ├─ FireEvent(GameStart)   ← EventHandler + 全局 Trigger 执行
   └─ LoadMap(startupMapId)

=== LOAD MAP (添加, 不替换) ===
3. GameEngine.LoadMap(mapId)
   ├─ MapManager.LoadMap → 合并 MapConfig
   ├─ CreateSession + Boards
   ├─ PushFocused → 旧 focused 变 Suspended + SuspendedTag
   ├─ 加载空间/地形/导航/实体
   ├─ 实例化 Map Trigger → ApplyDecorators → RegisterMapTriggers
   └─ FireMapEvent(mapId, MapLoaded, ctx)
       ├─ EventHandler 执行
       └─ Map-scoped Trigger 按 Priority 升序执行

=== UNLOAD MAP (显式移除) ===
4. GameEngine.UnloadMap(mapId)
   ├─ FireMapEvent(mapId, MapUnloaded, ctx)
   ├─ UnregisterMapTriggers
   ├─ MapSession.Cleanup (按 MapId 过滤销毁)
   └─ 恢复外层 Map → Active + FireMapEvent(MapResumed)
```

相关文档：

*   Pacemaker 时间与步进：见 [05_pacemaker.md](05_pacemaker.md)
*   ConfigPipeline 合并管线：见 [07_config_pipeline.md](07_config_pipeline.md)
*   Trigger 开发指南：见 [08_trigger_guide.md](08_trigger_guide.md)
*   Map 与空间服务：见 [10_map_mod_spatial.md](10_map_mod_spatial.md)
