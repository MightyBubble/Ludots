---
文档类型: 接口规范
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 04_游戏逻辑 - 导航寻路 - NodeGraph/TraversalPolicy/Overlay
状态: 草案
---

# NodeGraph/TraversalPolicy 接口规范

# 1 概述

## 1.1 本文档定义

本文档定义 NodeGraph（图数据结构）与 TraversalPolicy（遍历策略）的接口口径，目标是把图结构与规则/动态状态解耦，以便在大世界下保持只读图的稳定性，并通过 Overlay 实现动态封路与权重修正。

## 1.2 术语与约定

- NodeGraph：CSR（EdgeStart/EdgeTo/EdgeCost）+ SoA（字段按数组拆分）。
- TraversalPolicy：对 A* 提供 gate/cost/heuristic 的注入面。
- Overlay：动态封边/改权层；必须版本化。

## 1.3 关键约束

- 读阶段只读：求解阶段不得修改 NodeGraph 与 Overlay 容器。
- 代价不变量：cost 不得为 NaN；默认禁止负权。
- 过滤与改权必须可审计：policyId/overlayVersion 必须进入请求快照或结果统计。

# 2 核心接口

## 2.1 NodeGraph（数据形态）

### 签名

抽象字段族（用于口径，不代表必须公开为同名字段）：

```csharp
struct NodeGraph {
  int NodeCount;
  int EdgeCount;
  int[] EdgeStart;    // length = NodeCount + 1
  int[] EdgeTo;       // length = EdgeCount
  float[] EdgeBaseCost;
  int[] PosXcm; int[] PosYcm;
}
```

### 参数说明

| 字段/参数 | 类型 | 单位/范围 | 说明 |
|---|---|---|---|
| NodeCount | int | >0 | 节点数 |
| EdgeStart | int[] | [0,EdgeCount] | CSR 前缀和；必须单调非降 |
| EdgeTo | int[] | [0,NodeCount) | 边目标节点 |
| EdgeBaseCost | float[] | >=0 | 基础代价（默认非负，非 NaN） |
| PosXcm/PosYcm | int[] | cm | 节点世界坐标（厘米真源） |

### 返回值

无。

### 异常/错误码

数据不变量破坏必须 fail-fast（加载期/构建期拒绝）：

- EdgeStart 长度不为 NodeCount+1
- EdgeStart 非单调
- EdgeTo 超界
- EdgeBaseCost 为 NaN

### 线程安全性

- 求解读阶段为只读访问，可并行读取。
- 写入必须在资源写阶段进行，并与求解阶段隔离。

## 2.2 TraversalPolicy（gate/cost/heuristic 注入）

### 签名

```csharp
bool CanTraverseNode(int nodeId, in PolicyContext ctx);
bool CanTraverseEdge(int fromNodeId, int edgeIndex, int toNodeId, in PolicyContext ctx);
float GetEdgeCost(int fromNodeId, int edgeIndex, int toNodeId, float baseCost, in PolicyContext ctx);
float Heuristic(int nodeId, int goalNodeId, in PolicyContext ctx);
```

### 参数说明

| 参数名 | 类型 | 单位/范围 | 说明 |
|---|---|---|---|
| nodeId/fromNodeId/toNodeId | int | [0,NodeCount) | 节点索引 |
| edgeIndex | int | CSR 范围内 | 边索引 |
| baseCost | float | >=0 | 图的基础代价 |
| ctx | PolicyContext | struct | 纯值上下文（mask/policyId/overlayHandle 等） |

### 返回值

- gate：可通过 true，不可通过 false。
- GetEdgeCost：返回最终代价（必须非 NaN，默认非负）。

### 异常/错误码

- ctx 失效（例如 overlay handle generation 不匹配）时必须显式失败（NotReady/Error），禁止静默忽略。

### 线程安全性

- PolicyContext 必须为不可变值拷贝。
- 若 policy 内部引用 Overlay/TagSet 表，必须保证并行只读安全。

## 2.3 EdgeCostOverlay（动态封路/改权）

### 签名

```csharp
bool IsBlocked(int edgeGlobalId);
bool TryGetAddCost(int edgeGlobalId, out float delta);
bool TryGetMulCost(int edgeGlobalId, out float factor);
int  OverlayVersion { get; }
```

### 参数说明

| 参数名 | 类型 | 单位/范围 | 说明 |
|---|---|---|---|
| edgeGlobalId | int | [0,EdgeCount) | 全局边索引（或可映射索引） |
| delta | float | cost-unit | 代价叠加 |
| factor | float | >0 | 代价缩放 |

### 返回值

- 未命中则返回 false（表示无修正）。
- OverlayVersion 必须随写阶段更新递增（或等价策略）。

### 异常/错误码

- Overlay 写入不得在求解读阶段发生；违规视为严重错误（必须有证据入口）。

### 线程安全性

- 读阶段并行只读；写阶段集中修改并 bump 版本。

# 3 使用约束

## 3.1 生命周期与所有权

- NodeGraph 与 Overlay 属于 GraphStore 所有；上层不得持有可写引用。
- TraversalPolicy 不得捕获外部托管对象引用；所有语义通过 ID 化输入与只读表表达。

## 3.2 容量与预算

- TagSet/Overlay 表必须有容量上限；溢出必须在加载期拒绝或在写阶段熔断并可审计。
- 求解阶段必须记录 expanded/overlayVersion/policyId 用于统计。

## 3.3 失败策略

- 图数据不变量破坏：加载期拒绝（fail-fast）。
- policy/overlay 引用失效：返回 NotReady 或 Error（显式，不做静默 fallback）。

# 4 代码入口

## 4.1 接口定义位置

- `src/Core/Navigation/GraphCore/NodeGraph.cs`
- `src/Core/Navigation/GraphCore/NodeGraphTraversalPolicy.cs`
- `src/Core/Navigation/GraphCore/TagAndOverlayTraversalPolicy.cs`
- `src/Core/Navigation/GraphCore/GraphEdgeCostOverlay.cs`

## 4.2 关键实现位置

- `src/Core/Navigation/GraphCore/NodeGraphPathService.cs`

## 4.3 相关测试与对齐文档

- `docs/04_游戏逻辑/10_导航寻路/05_对齐报告/01_导航寻路_对齐报告.md`

