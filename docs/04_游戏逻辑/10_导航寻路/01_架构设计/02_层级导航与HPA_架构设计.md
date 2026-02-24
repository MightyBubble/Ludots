---
文档类型: 架构设计
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 04_游戏逻辑 - 导航寻路 - 层级导航与加速
状态: 草案
---

# 层级导航与HPA 架构设计

# 1 设计概述

## 1.1 本文档定义

本文档定义“层级导航（Hierarchical Pathfinding）”在 Ludots 大世界中的落地形态：如何将长距离规划与局部精确寻路拆分，并在不引入体系冲突的前提下提供可扩展的加速能力（例如 HPA* 思路）。

本文档为架构口径，不承诺当前版本已经实现完整 HPA*；实现以对齐报告为准。

## 1.2 设计目标

1. 长距离路径稳定：跨多个 chunk 的请求避免在 L2 上全域搜索。
2. streaming 友好：规划输出可驱动 corridor 与 loaded set，保证局部视图可控。
3. 可替换：L0/L1 的抽象方式可演进（ChunkGraph/PortalGraph/HPA ClusterGraph）。
4. 预算可控：层级规划与局部求解都可在预算内熔断并给出可审计降级结果。

## 1.3 设计思路

- L0（骨干层）先解决范围约束：输出粗路径（chunk/portal 序列）。
- L1 由粗路径扩张 corridor：决定局部装配范围并驱动 streaming。
- L2 在 corridor 内做精确求解：NodeGraph + TraversalPolicy/Overlay。

# 2 功能总览

## 2.1 术语表

| 术语 | 定义 | 备注 |
|---|---|---|
| ChunkGraph | 节点=chunk/portal 的粗图 | L0 |
| HPA* | 分块聚类+入口+抽象图的层级寻路思路 | 可选演进 |
| Corridor | 粗路径扩张得到的局部范围 | L1 |
| LoadedView | corridor 内装配出的局部图视图 | L2 |

## 2.2 功能导图

```
Request(start,goal)
  ├─ L0: PlanCoarsePath(ChunkGraph/HPA abstract) → coarsePath
  ├─ L1: BuildCorridor(coarsePath,radius,budget) → corridorChunks
  ├─ EnsureLoaded(corridorChunks) → loaded/not-ready
  └─ L2: SolveLocal(NodeGraph in corridor, policy, budget) → PathHandle
```

## 2.3 架构图

```
┌─────────────┐   ┌──────────────┐   ┌──────────────┐
│ Coarse Graph │→  │ Corridor      │→  │ Local Solve   │
│ (Chunk/HPA)  │   │ (budgeted)    │   │ (NodeGraph A*)│
└─────────────┘   └──────────────┘   └──────────────┘
```

## 2.4 关联依赖

- streaming/corridor：`src/Core/Navigation/GraphWorld/*`
- 局部求解：`src/Core/Navigation/GraphCore/*`
- 空间服务与 AOI：`src/Core/Navigation/AOI/*`、`src/Core/Spatial/*`
- 地图地形：`docs/04_游戏逻辑/09_地图与地形/*`

# 3 业务设计

## 3.1 业务用例与边界

用例：

- 大地图远距离移动（跨多个 chunk）。
- 目标移动/封路导致路径失效后的重算。

边界：

- L0/L1 只决定范围与粗路径，不关心局部动态规则细节（动态规则在 L2 通过 policy/overlay 体现）。

## 3.2 业务主流程

主流程图见 2.2。

## 3.3 关键场景与异常分支

- L0 不可达：直接返回 NoPath（不进入 L2）。
- corridor 超预算：降级为 CoarseOnly 或缩小半径（最多一次）。
- loaded 未就绪：返回 NotReady，等待 streaming 完成后重试。

# 4 数据模型

## 4.1 概念模型

- `CoarsePath`：chunk/portal 序列（固定上界或可审计长度）。
- `CorridorChunkSet`：chunk 集合（预算上限）。
- `LoadedViewHandle`：局部视图句柄（缓存命中与版本化）。

## 4.2 数据结构与不变量

- coarse 层节点规模必须受控；RouteTable 仅允许用于小规模 coarse 图。
- corridor 必须显式预算化；禁止隐式扩大搜索空间。

## 4.3 生命周期/状态机

- coarsePath 与 corridor 可缓存（以版本为 key）。
- loaded view 生命周期由 streaming 与 cache 策略控制（需版本化）。

# 5 落地方式

## 5.1 模块划分与职责

- CoarsePlanner：输出 coarsePath
- CorridorBuilder：输出 corridorChunks
- LoadedViewBuilder：装配/缓存局部视图
- LocalSolver：NodeGraph A* 求解并写入 PathStore

## 5.2 关键接口与契约

接口契约由本目录 02_接口规范与 04_裁决条款定义。

## 5.3 运行时关键路径与预算点

- coarse 规划预算（每帧最多处理的请求数、最大节点扩展）
- corridor 上限（radius/maxChunks）
- local A* maxExpanded

# 6 与其他模块的职责切分

## 6.1 切分结论

- streaming 与 AOI 决定能否装配视图；导航不绕开 streaming 直接求解全域。
- SpatialQuery 负责世界坐标投影；导航不直接依赖平台物理。

## 6.2 为什么如此

大世界的根因在范围控制与一致性边界；层级导航把根因前置解决。

## 6.3 影响范围

- `src/Core/Navigation/*`

# 7 当前代码现状

## 7.1 现状入口

- 局部求解：`src/Core/Navigation/GraphCore/NodeGraphPathService.cs`
- corridor 工具：`src/Core/Navigation/GraphWorld/GraphCorridorChunkSelector.cs`
- RouteTable（仅适用小图）：`src/Core/Navigation/GraphCore/GraphRouteTable*.cs`

## 7.2 差距清单

- coarse planner 接口与数据口径未固化（ChunkGraph/PortalGraph 的统一定义缺失）。
- LoadedView 缓存与版本化口径需要固化（避免频繁全量合图重建）。

## 7.3 迁移策略与风险

先固化 corridor 与 loaded view 预算口径，再引入 coarse planner（避免一开始做复杂 HPA）。

# 8 验收条款

1. 远距离请求必须先产出 coarsePath 与 corridor，且 corridor 有上限与审计字段。
2. streaming 未就绪时返回 NotReady，不允许隐式扩大到全域求解。
3. local 求解只在 corridor 内执行，并输出 expanded/maxExpanded 统计用于调参。

