---
文档类型: 架构设计
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 游戏逻辑 - 地图与地形 - Graph路网系统
状态: 草案
---

# Graph路网系统 架构设计

# 1 设计概述
## 1.1 本文档定义

本文档定义 Ludots 的“路网 Graph”口径：用于寻路与路网语义的导航图（NodeGraph/ChunkedStore/MultiLayerGraph/Overlay），以及它在地图加载后如何被选择性加载、如何多份叠加与如何注入 MapLoaded 上下文。

边界与非目标：

- 边界：只覆盖“路网真源与其运行时结构/叠加规则”，不覆盖具体 AI 决策与单位移动控制。
- 非目标：不把 GASGraph（技能/效果图）混入路网口径；两者必须在命名、上下文注入与文档中严格区分。

## 1.2 设计目标

- 多网络并存：同一张地图允许同时拥有多份路网（例如 `graph_road`、`graph_pedestrian`），供不同系统消费。
- 多层：支持 coarse/fine（HPA 或其他层级），用于路径粗化与走廊细化。
- 可叠加：支持静态 overlay（地图封路/施工）与运行期 overlay（动态封路/成本调整）。
- 可诊断：加载期失败能定位 GraphId、来源与证据路径。

## 1.3 设计思路

- 路网图以 `NodeGraph` 为最低层真源；以 `ChunkedNodeGraphStore` 支持按 chunk 装配视图；以 `MultiLayerGraph` 支持层级；以 `GraphEdgeCostOverlay` 支持叠加成本与封路。
- 地图加载只负责“把路网真源装配好并注入上下文”，上层系统在 MapLoaded 之后选择消费哪个 GraphId 与哪套 overlay。

# 2 功能总览
## 2.1 术语表

- GraphId：路网数据集标识（例如 `graph_road`）。
- NodeGraph：CSR SoA 的静态图结构（节点位置 cm、边 baseCost、tagSetId）。
- ChunkedNodeGraphStore：按 chunkKey 存储图块，并能拼装 LoadedGraphView。
- MultiLayerGraph：多层图（coarse/fine）与层间映射。
- Overlay：对边成本/封路的叠加（Add/Mul/Blocked）。

## 2.2 功能导图

- 图真源加载（可选，可多份）
- 分块装配（全量 view / corridor view）
- 多层寻路（粗层→细层）
- 叠加成本与封路（静态 overlay + 运行期 overlay）

## 2.3 架构图

```
GraphDataSet(GraphId)
  -> ChunkedNodeGraphStore(分块真源)
      -> LoadedGraphView(当前装配视图)
  -> MultiLayerGraph(可选，多层)
  -> GraphEdgeCostOverlay(可选，叠加)
```

## 2.4 关联依赖

- GraphCore：`src/Core/Navigation/GraphCore/*`
- GraphWorld：`src/Core/Navigation/GraphWorld/*`
- MultiLayerGraph：`src/Core/Navigation/MultiLayerGraph/*`
- Overlay：`src/Core/Navigation/GraphCore/GraphEdgeCostOverlay.cs`
- GAS overlay sink（运行期写入）：`src/Core/Navigation/GraphSemantics/GAS/GraphEdgeCostOverlaySink.cs`

# 3 业务设计
## 3.1 业务用例与边界

- 用例：地图加载后，导航系统拿到 `graph_road` 的路网图；战斗/施工系统通过 overlay 动态封路或提高成本；寻路在 coarse/fine 层级上完成路径规划。
- 边界：路网图不负责决定“走哪里更聪明”，只提供可查询、可叠加的路径代价与可达性约束。

## 3.2 业务主流程

```
LoadMap(mapId)
  -> ResolveMapDataSets(MapConfig) 得到 Graph 数据集列表（可为空，可多份）
  -> LoadGraphs(...) 构建 GraphRegistry 并装配每个 GraphId 的运行时结构
  -> ctx.Set(GraphRegistry) 在 MapLoaded 上下文注入
```

## 3.3 关键场景与异常分支

- 图块缺失：声明了某 GraphId 但缺少必要 chunk 资产时，加载期失败并给出证据。
- 多来源冲突：同一 GraphId 的资产来源不兼容或策略缺失时，不允许 silent override。
- overlay 越界：overlay 的 edgeCount 必须与装配后的 Graph.EdgeCount 匹配，否则属于配置错误，应失败并定位。

# 4 数据模型
## 4.1 概念模型

- GraphRegistry：地图加载后的运行时容器，按 GraphId 管理多份路网。
- GraphInstance：GraphId 对应的一份路网实例，包含：
  - 结构真源（ChunkedStore/LoadedView）
  - 可选的多层结构（MultiLayerGraph）
  - 可选的 overlay（GraphEdgeCostOverlay，支持多来源叠加）

## 4.2 数据结构与不变量

- NodeGraph 的位置单位为厘米（cm），需与引擎坐标口径一致。
- overlay 的容量必须 >= edgeCount，Blocked 语义必须一致（0=可走，1=封路）。

## 4.3 生命周期/状态机

- 未加载：GraphRegistry 不存在或为空。
- 已加载：MapLoaded 注入后，上层系统可查询/寻路并应用 overlay。
- 切图：加载新 MapId 时替换为新地图对应的 GraphRegistry（按 map 维度隔离）。

# 5 落地方式
## 5.1 模块划分与职责

- GraphCore：静态图与寻路（A*/RouteTable）。
- GraphWorld：分块拼装与局部视图（LoadedGraphView）。
- MultiLayerGraph：层级寻路服务。
- GraphSemantics（可选）：把高层语义（tags/attributes）投影到 overlay（例如 GAS sink）。

## 5.2 关键接口与契约

- MapLoaded 上下文应提供 GraphRegistry（或明确该地图不提供任何路网）。
- 同一 GraphId 的叠加必须显式 id + 合并策略；禁止 silent override。
- 必须区分“路网 Graph”与“GASGraph 程序加载”：后者由 `GraphProgramLoader` 加载到 `GraphProgramRegistry`，属于技能图运行时，不属于路网真源。

## 5.3 运行时关键路径与预算点

- A* 应做到 warmup 后零分配（已有相关测试基线）。
- corridor view 的 chunk 选择要避免过大半径导致装配膨胀。

# 6 与其他模块的职责切分
## 6.1 切分结论

- Hex(VertexMap) 负责地表真源；Graph 负责路网真源；二者都可选、互不依赖，但都必须使用统一坐标/单位口径（厘米域）。

## 6.2 为什么如此

路网不应从地形推导为唯一形式：同一地形上可存在多套语义路网（道路/人行道/水路），并且路网需要更强的“业务可解释性与可叠加”能力。

## 6.3 影响范围

- 导航/AI 系统必须在 MapLoaded 后从 GraphRegistry 获取所需 GraphId，而不是自行在运行期隐式构建。

# 7 当前代码现状
## 7.1 现状入口

- NodeGraph：`src/Core/Navigation/GraphCore/NodeGraph.cs`
- ChunkedStore：`src/Core/Navigation/GraphWorld/ChunkedNodeGraphStore.cs`
- MultiLayerGraph：`src/Core/Navigation/MultiLayerGraph/MultiLayerGraph.cs`
- Overlay：`src/Core/Navigation/GraphCore/GraphEdgeCostOverlay.cs`
- GAS overlay sink：`src/Core/Navigation/GraphSemantics/GAS/GraphEdgeCostOverlaySink.cs`
- 测试基线：`src/Tests/GasTests/GraphNetworkTests.cs`

## 7.2 差距清单

- 地图级接线缺失：当前引擎 LoadMap 流程未加载任何“路网 Graph 数据集”，Graph 相关结构主要存在于库代码与测试中。
- 文档边界易混淆：项目中已接入的 `GraphProgramLoader` 属于 GASGraph（技能图），容易与路网 Graph 概念混用，需要在文档与 ContextKeys 上做明确隔离。

## 7.3 迁移策略与风险

- 先补齐：GraphRegistry/GraphId 命名与 MapLoaded 注入契约文档。
- 再落地：最小路网资产格式与加载入口（分块/多层/overlay 至少先完成一种），避免只停留在“库代码可用但无法在地图中使用”的状态。

# 8 验收条款

- 同一地图可同时加载多份 GraphId，并且 MapLoaded 上下文可稳定定位到它们。
- overlay 可叠加且可诊断：无策略/不兼容时 fail-fast，错误信息包含 GraphId 与来源证据。
- 文档与命名清晰区分路网 Graph 与 GASGraph，避免业务侧错误依赖。
