---
文档类型: 架构设计
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 04_游戏逻辑 - 导航寻路 - NavGraph 生成与运行时闭环
状态: 草案
---

# NavGraph 生成方案 架构设计

# 1 设计概述

## 1.1 本文档定义

本文档定义 Ludots 的 NavGraph/NavTile 数据生成与运行时消费的闭环方案，关注：

- 生成什么数据（tile/navmesh/portal/抽象图）
- 怎么生成（输入→处理→产物）
- 如何在 64km×64km 大世界下加载/更新/查询（预算与确定性）

边界：

- 本文档不绑定第三方实现，不引入业务语义。
- 本文档不替代空间服务与物理系统的口径真源；导航只消费其规则化输入/产物。

## 1.2 设计目标

1. 64km 可扩展：必须按 chunk/tile 分块，支持按需加载与局部更新。
2. 闭环可交付：bake → 导出 → 加载 → 查询 的链路完整，数据版本强校验。
3. 预算与确定性：查询固定容量、排序口径固定、更新可分帧并可观测。
4. 分层组合：世界级粗路径与局部连续域移动分离，避免全局单 mesh 的不可维护性。

## 1.3 设计思路

- 抽取外部方案的能力分层与数据生命周期，不照搬实现细节。
- 将能力映射到项目 SSOT：地图地形、空间服务、物理系统、GraphCore/GraphWorld。
- 对缺失环节补齐：烘焙产物格式、运行时服务接口、动态更新语义，保证闭环可运行。

# 2 功能总览

## 2.1 术语表

| 术语 | 含义 | 说明 |
|------|------|------|
| Tile | 导航分块单元 | 与地图 chunkKey/tileKey 对齐 |
| NavTile | 一个 tile 的可走域数据 | vertices/polys/邻接/portal/版本 |
| Portal | 跨 tile 边界连接 | 形成 tile 间走廊 |
| NavGraph | 用于寻路的图表示 | fine（poly 邻接）+ coarse（抽象节点） |
| Coarse Path | 世界级粗路径 | 低频，跨 chunk，服务 streaming |
| Fine Move | 局部连续域移动 | 走廊跟随/平滑/局部再规划 |

## 2.2 功能导图

```
地图真源（Map/Terrain）
  │
  ├─ Bake: Walkable Domain（规则 + 派生）
  │     └─ 生成：边界/holes/禁行区
  │
  ├─ Build: NavTile（三角化/邻接/portal）
  │     ├─ polys + neighbors
  │     ├─ portals（跨 tile 边界）
  │     └─ tileVersion
  │
  └─ Export: NavTileBin（版本化）
        │
        ▼
Runtime Load（AOI/Streaming）
  │
  ├─ Fine Query（nearest / corridor / raycast）
  │
  └─ Coarse Graph（GraphCore/HPA）→ 跨 tile/跨 chunk 路径
```

## 2.3 架构图

```
┌─────────────────────────────┐
│ Map/Terrain                  │  几何真源 + 派生规则
└──────────────┬──────────────┘
               │ tileKey/chunkKey
               ▼
┌─────────────────────────────┐
│ NavBakeTool                  │  离线/工具侧
│ - walkable extraction        │
│ - triangulation              │
│ - adjacency + portals        │
└──────────────┬──────────────┘
               │ NavTileBin（版本化）
               ▼
┌─────────────────────────────┐
│ Runtime NavTileStore         │  tile cache + version
├─────────────────────────────┤
│ NavQueryService              │  nearest/raycast/corridor/path
└──────────────┬──────────────┘
               │ coarse requests
               ▼
┌─────────────────────────────┐
│ GraphCore/HPA                │  世界级 coarse path
└─────────────────────────────┘
```

## 2.4 关联依赖

- 地图与地形：`docs/04_游戏逻辑/09_地图与地形/*`
- 空间服务：`docs/03_基础服务/06_空间服务/*`
- 物理系统：`docs/03_基础服务/07_物理系统/*`
- 导航运行时：`src/Core/Navigation/*`

# 3 业务设计

## 3.1 业务用例与边界

用例（系统级）：

- 单位从 A 走到 B：需要 coarse path（跨 chunk）+ fine move（连续域）。
- 单位在局部区域绕障：需要 fine query（走廊/局部再规划）。
- 地形被修改：只重建受影响 tile，并在运行时热切换。

边界：

- NavGraph 只表达可走域与代价；不表达攻击目标/技能释放等业务语义。
- 物理/碰撞属于不同子系统；导航只消费规则化输入与其派生产物。

## 3.2 业务主流程

```
StartMove(A,B)
  → EnsureLoaded(AOI tiles around actor)
  → CoarsePlan: chunk/portal graph across chunks (optional)
  → FineLocate: nearest poly in current tile
  → Follow corridor across portals
  → If tileVersion changed / blocked: re-locate & re-plan
```

## 3.3 关键场景与异常分支

- 无可走域：nearest 失败 → 返回不可达（由上层决定退化：停留/换目标）。
- buffer 超限：查询返回 dropped>0 → 上层按裁决退化（降频/分帧/扩大范围）。
- tile 热切换：tileVersion 不一致 → corridor 失效 → 触发 re-locate/re-plan。

# 4 数据模型

## 4.1 概念模型

- NavTile：tile 内多边形集合（tri/poly）+ 邻接关系 + portal。
- Portal：两侧 poly 的连接边界（跨 tile）。
- NavGraph：fine（poly 邻接）与 coarse（抽象节点）双表示组合。

## 4.2 数据结构与不变量

NavTile 最小字段集合（建议）：

- tileKey（与地图 chunkKey 一致）
- tileBounds（cm）
- vertices
- polys（index + neighbor + flags/area）
- portals（边界连接）
- tileVersion（缓存失效）

不变量：

- 不允许全局单 mesh 常驻；必须可按 tile 载入/卸载。
- 导出格式必须含 magic/version；版本不匹配必须 fail-fast。

## 4.3 生命周期/状态机

- Bake（工具侧）：生成 NavTileBin（版本化）。
- Load（运行时）：tileKey 进入 AOI → 加载到 NavTileStore。
- Update（运行时）：地形修改 → dirty tile 重建 → tileVersion++ → 原子替换。
- Unload：tileKey 离开 AOI → 释放 tile（可缓存策略化）。

# 5 落地方式

## 5.1 模块划分与职责

- Tool：NavBakeTool
  - 输入：Map/Terrain 真源 + walkability 规则
  - 输出：NavTileBin（按 tile）
- Runtime：NavTileStore/NavQueryService
  - 加载：按 AOI/Streaming 载入 NavTileBin
  - 查询：nearest/raycast/corridor/path（固定容量）
- Coarse：GraphCore/HPA
  - 世界级跨 chunk 路径（低频）
  - 仅依赖抽象节点图，避免高频访问所有 poly

## 5.2 关键接口与契约

必须具备的接口族（口径级）：

- LoadTile(tileKey) / UnloadTile(tileKey)
- TryProject(worldPosCm) → NavLocation（携带 tileVersion）
- TryBuildCorridor(start,goal,corridorBudget) → Corridor（含 portal 序列）
- TrySolveCoarsePath(chunkA,chunkB,budget) → CoarsePath

## 5.3 运行时关键路径与预算点

- AOI 触发的 tile load/unload 上限（每帧 K 个）
- corridor 半径与最大 tile/chunk 数上限
- fine query 候选上限与 dropped 统计口径
- dirty tile 重建预算（每帧最多重建 M 个 tile）

# 6 与其他模块的职责切分

## 6.1 切分结论

- Map/Terrain 是几何真源；导航产物必须由其派生，运行时不直接修改真源。
- 空间服务提供投影与查询；导航不直接依赖平台物理 API。
- 物理系统提供障碍与碰撞真源；导航只消费规则化输入与其派生产物。

## 6.2 为什么如此

大世界的复杂度来自数据规模、streaming 与动态更新。将真源与派生分离，才能做到版本化热切换、缓存失效与稳定预算。

## 6.3 影响范围

受影响模块入口：

- `src/Core/Navigation/*`
- `src/Core/Spatial/*`

# 7 当前代码现状

## 7.1 现状入口

- 导航运行时入口：`src/Core/Navigation/*`
- 图与求解：`src/Core/Navigation/GraphCore/*`
- 分块与 corridor：`src/Core/Navigation/GraphWorld/*`

## 7.2 差距清单

- 缺少明确的 NavTileBin 版本化格式与加载期强校验口径。
- 缺少 dirty tile → 分帧重建 → 原子替换 的统一更新管线口径。

## 7.3 迁移策略与风险

- 优先补齐导出/加载/版本校验的闭环，再做动态更新与局部重建。
- 风险：若未先固化 tileKey 与版本化，后续缓存与 corridor 无法可靠工作。

# 8 验收条款

1. Bake→导出→加载→查询闭环可跑通，NavTileBin 版本不匹配可明确失败。
2. streaming 下 tile 的 load/unload 与 corridor 规模都受预算上限约束并可审计。
3. 动态地形修改只影响 dirty tile，且可分帧重建并进行版本化热切换。

