---
文档类型: 接口规范
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 04_游戏逻辑 - 导航寻路 - NavQueryService
状态: 草案
依赖文档:
  - docs/04_游戏逻辑/10_导航寻路/01_架构设计/03_NavGraph生成方案_架构设计.md
  - docs/04_游戏逻辑/10_导航寻路/01_架构设计/04_CDTNavMeshTileBake_架构设计.md
---

# NavQueryService 接口规范

# 1 概述
## 1.1 目标
- 定义运行时导航查询的接口族，保证 64km×64km 场景下可预算、可统计、可降级。
- 统一失败语义与 dropped 统计，避免隐式扩容与静默 fallback。

## 1.2 基本约束
- 查询以厘米坐标输入输出，平面为 XZ，返回路径为折线点序列。
- 服务实现必须是可重入只读查询，写阶段通过显式的 tile 热切换安全点完成。

# 2 核心接口族
## 2.1 Tile 生命周期
- `LoadTile(tileId)`：将 tile 载入缓存，若缺资产返回失败码与证据。
- `UnloadTile(tileId)`：从缓存移除 tile，允许 LRU 驱动的自动卸载。

## 2.2 定位与投影
- `TryProject(worldXcm, worldZcm, out NavLocation loc)`：
  - 成功时返回 `NavLocation`，必须携带 `tileId` 与 `tileVersion`。
  - 失败时返回 `NotReady` 或 `NoWalkableDomain`。

NavLocation 最小字段：
- `TileId`
- `TileVersion`
- `TriangleId`
- `LocalXcm, LocalZcm` 或等价 barycentric 表达

## 2.3 走廊与路径
- `TryBuildCorridor(startLoc, goalLoc, CorridorBudget budget, out Corridor corridor)`：
  - corridor 必须显式包含 tile 序列与 portal 序列。
  - 超预算必须返回 `BudgetExceeded` 并包含 dropped 统计与降级原因枚举。

- `TryFindPath(startXcm,startZcm,goalXcm,goalZcm, AgentSpec agent, PathBudget budget, out PathResult result)`：
  - 结果必须包含：状态码、折线点数组、expanded/dropped、降级原因、使用的 tileVersion 快照。

## 2.4 Raycast 与局部查询
- `TryRaycast(fromXcm,fromZcm,toXcm,toZcm, AgentSpec agent, out RaycastHit hit)`：
  - 用于直线路径可行性、局部避障与视线裁决。

# 3 预算与统计
## 3.1 预算字段
CorridorBudget：
- `MaxTiles`
- `MaxPortals`
- `MaxExpandedL0`
- `MaxExpandedL2PerTile`

PathBudget：
- `MaxExpandedTotal`
- `MaxResultPoints`

## 3.2 统计字段
所有查询必须提供：
- `Expanded`：A* 扩展节点数
- `Dropped`：队列溢出或结果截断计数
- `BudgetExceeded`：是否触发超限

# 4 失败语义
状态码枚举建议：
- `Ok`
- `NotReady`
- `NoWalkableDomain`
- `NotReachable`
- `BudgetExceeded`
- `InvalidInput`
- `TileVersionMismatch`

规则：
- tileVersion mismatch 触发必须返回明确状态码，调用方决定 re-locate/re-plan。
- 禁止在服务内部静默扩大 corridor 或扩大队列容量。
