# Map、Mod 与空间服务可插拔

本篇说明三件事：

*   Map 与 Mod 的关系：地图配置如何从 Core 与 Mods 合并得到。
*   地图切换的生命周期：旧地图如何清理，新地图如何加载与生成实体。
*   空间服务可插拔：为什么可以按地图参数拆装空间服务，以及系统如何避免持有旧引用。

## 1 Map 与 Mod 的关系

在 Ludots 中，MapConfig 不是单一来源文件，而是可合并的配置对象：

*   Core 提供基础 MapConfig。
*   Mod 可以提供同名 MapConfig 片段，覆盖或扩展某些字段。
*   MapConfig 支持 `ParentId`，用于继承与分层复用。

MapConfig 的加载与合并由 `MapManager.LoadMap(mapId)` 完成。

## 2 地图切换生命周期

地图加载入口是 `GameEngine.LoadMap(mapId)`。

### 2.1 清理旧地图

如果已有 `CurrentMapSession`，会先执行清理：

*   销毁所有带 `MapEntity` 的实体（上一张地图生成的实体会统一打上这个标签）。
*   清空空间分区（spatialPartition.Clear）。
*   重置 LoadedChunks 的 SSOT（HexGridAOI.Reset），通知依赖“已加载区域”的系统回到空态。

清理逻辑集中在 `MapSession.Cleanup`。

### 2.2 加载新地图配置并创建 MapSession

加载流程要点：

1.  读取并合并 MapConfig：`MapManager.LoadMap(mapId)`。
2.  创建新的 `MapSession` 并写入 `GlobalContext`（供 Trigger 与系统读取）。
3.  如地图提供了空间参数（MapConfig.Spatial），执行空间服务热切换。
4.  加载地形与导航相关数据（例如 VertexMap、NavMesh）。
5.  根据 map config 的 entities/templates 创建实体：`MapLoader.LoadEntities(mapConfig)`。
6.  触发 `GameEvents.MapLoaded`。

## 3 空间服务可插拔

空间服务可插拔的核心思想是：空间服务是“按地图参数生成的运行时服务”，它不应该被当作全局静态单例写死。

### 3.1 可插拔的服务对象

当地图提供自定义空间参数时，引擎会重建并替换以下服务：

*   `WorldSizeSpec`：世界 AABB 与格子尺寸（cm）。
*   `SpatialCoordinateConverter`：坐标转换器。
*   `ChunkedGridSpatialPartitionWorld`：空间分区存储。
*   `SpatialQueryService`：空间查询服务（并通过 WireUpPositionProvider 绑定实体位置读取）。
*   如果空间类型为 Hex/Hybrid，还会注入 `HexMetrics` 到查询服务与 GlobalContext。

这一段逻辑集中在 `GameEngine.ApplyMapSpatialConfig`。

### 3.2 为什么需要“热切换点”

系统如果在构造时缓存了空间服务引用，那么地图切换后会出现“系统仍在使用旧空间”的问题。因此引擎在重建空间服务后，会显式把依赖注入到系统的可替换字段中：

*   `WorldToGridSyncSystem.SetCoordinateConverter(...)`
*   `SpatialPartitionUpdateSystem.SetPartition(...)`

这样系统在后续 Update 中使用的就是“新地图的空间服务”，而不是旧引用。

## 4 LoadedChunks 的 SSOT

Ludots 使用 `HexGridAOI` 作为 “已加载区域” 的单一事实来源（SSOT），并把它写入 `GlobalContext`。

地图切换时通过 `HexGridAOI.Reset()` 让所有依赖方回到空态，这避免了跨地图残留的订阅与状态泄漏。

## 5 与配置与启动顺序的关系

*   地图最终会依赖 MergedConfig（例如默认格子大小、功能开关、启动地图 ID）。
*   MapLoaded 是 Trigger 的关键节点，适合做“按地图 tags 初始化”的逻辑。

相关文档：

*   启动顺序与入口点：见 [09_startup_entrypoints.md](09_startup_entrypoints.md)
*   Trigger 开发指南：见 [08_trigger_guide.md](08_trigger_guide.md)

