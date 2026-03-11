# Map、Mod 与空间服务可插拔

本篇说明三件事：

*   Map 与 Mod 的关系：地图配置如何从 Core 与 Mods 合并得到。
*   地图生命周期：Map 并存模型、焦点栈、加载与卸载流程。
*   空间服务可插拔：为什么可以按地图参数拆装空间服务，以及系统如何避免持有旧引用。

## 1 Map 与 Mod 的关系

在 Ludots 中，MapConfig 不是单一来源文件，而是可合并的配置对象：

*   Core 提供基础 MapConfig。
*   Mod 可以提供同名 MapConfig 片段，覆盖或扩展某些字段。
*   MapConfig 支持 `ParentId`，用于继承与分层复用。

MapConfig 的加载与合并由 `MapManager.LoadMap(mapId)` 完成。

## 2 Map 并存模型

引擎支持**添加式地图加载**——`LoadMap` 不再替换旧地图，多个 Map 可以同时存在。`MapSessionManager` 使用**焦点栈**管理活跃状态。

### 2.1 MapSessionManager 焦点栈

```
MapSessionManager
  ├── "strategic" (Suspended)  ← 战略图暂停但保留
  ├── "battle_42" (Active)     ← 战斗副本在栈顶，有焦点
  └── _focusStack: [strategic, battle_42]
```

*   `CreateSession(mapId, mapConfig)` — 创建会话
*   `PushFocused(mapId)` — 入栈，旧栈顶变 Suspended
*   `PopFocused()` — 弹栈，新栈顶恢复 Active
*   `UnloadSession(mapId, world)` — 清理会话并从栈中移除

### 2.2 MapSession

每个 MapSession 持有：
*   `MapId` 和 `MapConfig`
*   `Board` 集合（空间域抽象，如 HexBoard、Grid2DBoard）
*   `Trigger` 列表（Map 声明的触发器）
*   `MapContext`（层级化上下文）
*   `State`（Active / Suspended / Disposed）

### 2.3 SuspendedTag 实体管理

当 Map 被 Suspend 时，其实体会被加上 `SuspendedTag` 组件；恢复 Active 时移除。这允许 System 通过 `WithNone<SuspendedTag>()` 过滤暂停的实体。

## 3 地图加载生命周期

### 3.1 LoadMap — 添加式加载

```
GameEngine.LoadMap(mapId)
  ├── MapManager.LoadMap → 合并 MapConfig
  ├── MapSessionManager.CreateSession(mapId, mapConfig)
  ├── 创建 Boards (BoardFactory)
  ├── PushFocused(mapId) → 旧 focused 变 Suspended + SuspendedTag
  ├── 应用空间配置 (ApplyBoardSpatialConfig)
  ├── 加载地形/导航数据
  ├── MapLoader.LoadEntities(mapConfig) → 创建实体
  ├── 实例化 Map Trigger → 应用 TriggerDecorators
  ├── RegisterMapTriggers(mapId, triggers)
  └── FireMapEvent(mapId, MapLoaded, ctx)
      ├── EventHandler 执行（所有 Mod 的 OnEvent 回调）
      └── Map-scoped Trigger 执行（按 Priority 升序）
```

**如果同 ID 的 Map 已存在**，会先 `UnloadMap` 再重新加载。

### 3.2 UnloadMap — 显式卸载

```
GameEngine.UnloadMap(mapId)
  ├── FireMapEvent(mapId, MapUnloaded, ctx)
  ├── UnregisterMapTriggers(mapId)
  ├── MapSessionManager.UnloadSession(mapId, world)
  │   ├── MapSession.Cleanup(world) → 按 MapId 过滤销毁实体
  │   ├── Board.Dispose()
  │   └── RemoveFromFocusStack(mapId)
  └── 如果 mapId 在栈顶：恢复下一个 → Active + 移除 SuspendedTag
      ├── 重新加载空间配置和导航
      └── FireMapEvent(restoredMapId, MapResumed, ctx)
```

### 3.3 实体清理的 MapId 过滤

`MapSession.Cleanup(world)` 使用两阶段清理：
1.  收集 `MapEntity.MapId == targetMapId` 的实体
2.  逐个销毁

这确保卸载一张 Map 不会影响其他 Map 的实体。

### 3.4 PushMap / PopMap — 嵌套地图

`PushMap(innerMapId)` 和 `PopMap()` 用于嵌套场景（如战略→战斗）：
*   PushMap：暂停外层 Map（SuspendedTag），加载内层 Map
*   PopMap：卸载内层 Map，恢复外层 Map 的空间/导航/Active 状态

## 4 空间服务可插拔

空间服务可插拔的核心思想是：空间服务是"按地图参数生成的运行时服务"，它不应该被当作全局静态单例写死。

### 4.1 Board 抽象

Board 抽象将空间域与 Map 概念分离：

*   `IBoard`：空间域接口（Name、Dispose）
*   `HexBoard`：六角格 Board，持有 VertexMap
*   `Grid2DBoard`：2D 网格 Board
*   每个 MapSession 可以包含多个 Board

### 4.2 可插拔的服务对象

当地图提供自定义空间参数时，引擎会通过 `ApplyBoardSpatialConfig` 重建并替换以下服务：

*   `WorldSizeSpec`：世界 AABB 与格子尺寸（cm）。
*   `SpatialCoordinateConverter`：坐标转换器。
*   `ChunkedGridSpatialPartitionWorld`：空间分区存储。
*   `SpatialQueryService`：空间查询服务。
*   如果空间类型为 Hex/Hybrid，还会注入 `HexMetrics` 到查询服务与 GlobalContext。

### 4.3 单 Board 地图也必须显式声明 `Boards`

当前引擎只会在 `mapConfig.Boards` 非空时创建地图自己的 Board：

*   `src/Core/Engine/GameEngine.cs:1042`
*   `src/Core/Engine/GameEngine.cs:1044`

随后也只有在 `session.PrimaryBoard != null` 时，才会把该地图的空间配置应用到引擎级空间服务：

*   `src/Core/Engine/GameEngine.cs:771`
*   `src/Core/Engine/GameEngine.cs:774`

这意味着，如果一张地图没有声明 `Boards`：

*   当前 map session 不会有 primary board
*   `ApplyBoardSpatialConfig(...)` 不会执行
*   实体仍会运行在“此前已生效的空间域”上

对启动地图来说，“此前已生效的空间域”通常是启动阶段或全局默认空间；如果地图实体坐标超出那个空间域，`SpatialPartitionUpdateSystem` 会直接抛 `SPATIAL.ERR.WorldPositionOutOfBounds`：

*   `src/Core/Systems/SpatialPartitionUpdateSystem.cs:56`
*   `src/Core/Systems/SpatialPartitionUpdateSystem.cs:103`

因此，**哪怕只是最简单的单 Board 地图，也必须显式写出 `Boards`**。这不是验收专用约定，而是当前引擎空间域切换的正式合同。

相机验收地图本次补全了 6 张地图的 `Boards` 声明，作为可运行示例：

*   `mods/fixtures/camera/CameraAcceptanceMod/assets/Maps/camera_acceptance_projection.json:4`
*   `mods/fixtures/camera/CameraAcceptanceMod/assets/Maps/camera_acceptance_rts.json:4`
*   `mods/fixtures/camera/CameraAcceptanceMod/assets/Maps/camera_acceptance_tps.json:4`
*   `mods/fixtures/camera/CameraAcceptanceMod/assets/Maps/camera_acceptance_blend.json:4`
*   `mods/fixtures/camera/CameraAcceptanceMod/assets/Maps/camera_acceptance_follow.json:4`
*   `mods/fixtures/camera/CameraAcceptanceMod/assets/Maps/camera_acceptance_stack.json:4`

测试也显式校验了地图已经声明 `PrimaryBoard`：

*   `src/Tests/GasTests/Production/CameraAcceptanceModTests.cs:255`

### 4.4 为什么需要"热切换点"

系统如果在构造时缓存了空间服务引用，那么地图切换后会出现"系统仍在使用旧空间"的问题。因此引擎在重建空间服务后，会显式把依赖注入到系统的可替换字段中：

*   `WorldToGridSyncSystem.SetCoordinateConverter(...)`
*   `SpatialPartitionUpdateSystem.SetPartition(...)`

这样系统在后续 Update 中使用的就是"新地图的空间服务"，而不是旧引用。

### 4.5 Grid Board 尺寸坑点

`GridBoard` 的世界尺寸不是简单的 `WidthInTiles * GridCellSizeCm`，而是：

*   `worldWidthCm = WidthInTiles * 256 * GridCellSizeCm`
*   `worldHeightCm = HeightInTiles * 256 * GridCellSizeCm`

代码位置：

*   `src/Core/Map/Board/GridBoard.cs:26`
*   `src/Core/Map/Board/GridBoard.cs:27`
*   `src/Core/Map/Board/GridBoard.cs:28`

因此在默认 `GridCellSizeCm = 100` 时：

*   `WidthInTiles = 1`
*   `HeightInTiles = 1`

已经对应 `25600cm x 25600cm` 的世界空间，足够覆盖多数 demo / showcase / 小型对战地图。

如果作者误以为这里的 `WidthInTiles` 是“单个逻辑格子数量”，就容易把 board 配置得比预期大很多，或者在未声明 `Boards` 时错误推断世界边界来自地图实体分布。这两个认知都会导致空间问题排查变得困难。

## 5 LoadedChunks 的 SSOT

Ludots 使用 `HexGridAOI` 作为 "已加载区域" 的单一事实来源（SSOT），并把它写入 `GlobalContext`。

地图切换时通过 `HexGridAOI.Reset()` 让所有依赖方回到空态，这避免了跨地图残留的订阅与状态泄漏。

## 6 Map 事件隔离

`FireMapEvent(mapId, eventKey, ctx)` 只触发：
1.  **EventHandler**——Mod 通过 `context.OnEvent()` 注册的回调（跨 Map）
2.  **Map-scoped Trigger**——通过 `RegisterMapTriggers(mapId, triggers)` 注册的触发器

**不会触发**全局注册的 Trigger 或其他 Map 的 Trigger。这确保了并存 Map 之间的事件隔离。

## 7 与配置与启动顺序的关系

*   地图最终会依赖 MergedConfig（例如默认格子大小、功能开关、启动地图 ID）。
*   MapLoaded 是 EventHandler 的关键节点，适合做"按地图 tags 初始化"的逻辑。

相关文档：

*   启动顺序与入口点：见 [启动顺序与入口点](startup_entrypoints.md)
*   Trigger 开发指南：见 [Trigger 开发指南](trigger_guide.md)

