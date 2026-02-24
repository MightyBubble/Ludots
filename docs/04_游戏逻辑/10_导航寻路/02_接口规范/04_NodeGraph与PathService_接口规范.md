---
文档类型: 接口规范
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 04_游戏逻辑 - 导航寻路 - NodeGraph/PathService
状态: 草案
---

# NodeGraph/PathService 接口规范

# 1 概述

## 1.1 本文档定义

本文档定义 NodeGraph（图数据形态）与 PathService（求解入口）的接口口径，确保图数据与求解过程满足：只读并行安全、预算可控、确定性与可审计。

## 1.2 术语与约定

- NodeGraph：CSR+SoA 的节点图（节点厘米坐标）。
- PathService：给定请求（start/goal + policy + budget）输出 PathResult 与 PathHandle。
- EdgeIndex：CSR 的全局边索引。

## 1.3 关键约束

- 图数据不变量必须在加载期/构建期校验；运行时不做静默容错。
- 求解必须携带 maxExpanded；超限返回 BudgetExceeded。
- 求解不得修改图数据；写阶段更新必须与求解阶段隔离。

# 2 核心接口

## 2.1 NodeGraph（CSR+SoA 数据形态）

### 签名

```csharp
NodeCount: int
EdgeCount: int
EdgeStart: int[NodeCount+1]
EdgeTo: int[EdgeCount]
EdgeBaseCost: float[EdgeCount]
PosXcm/PosYcm: int[NodeCount]
```

### 参数说明

| 参数名 | 类型 | 单位或取值范围 | 说明 |
|---|---|---|---|
| NodeCount | int | >0 | 节点数 |
| EdgeStart | int[] | 单调非降 | CSR 前缀和，长度=NodeCount+1 |
| EdgeTo | int[] | [0,NodeCount) | 目标节点 |
| EdgeBaseCost | float[] | >=0 且非 NaN | 基础代价 |
| PosXcm/PosYcm | int[] | cm | 节点坐标（厘米真源） |

### 返回值

无。

### 异常/错误码

不变量破坏必须 fail-fast（加载/构建拒绝），包括但不限于：

- EdgeStart 非单调或长度不匹配
- EdgeTo 越界
- EdgeBaseCost 为 NaN

### 线程安全性

求解读阶段为只读访问，可并行读取。

## 2.2 PathService（求解入口）

### 签名

伪代码签名：

```csharp
bool TrySolve(
  in NodeGraph graph,
  in PathRequest request,
  in TraversalPolicy policy,
  in PathBudget budget,
  out PathResult result);
```

### 参数说明

| 参数名 | 类型 | 单位或取值范围 | 说明 |
|---|---|---|---|
| graph | NodeGraph | 只读 | 目标图 |
| request | PathRequest | 见 02 文档 | 起点/终点/版本快照等 |
| policy | TraversalPolicy | struct | gate/cost/heuristic 注入 |
| budget | PathBudget | >0 | maxExpanded 等预算 |
| result | PathResult(out) | 见 02 文档 | 状态/handle/统计 |

### 返回值

- true：求解执行完成（Found/NoPath/BudgetExceeded 等在 result.Status 表达）
- false：输入非法或依赖未就绪（必须给出可审计错误码）

### 异常/错误码

- InvalidRequest：请求字段非法（缺失 GraphId、预算为 0 等）
- NotReady：依赖未就绪（未加载/corridor 缺失/投影失败）

### 线程安全性

- 求解可并行（每线程 workspace），但结果写回与 PathStore 回收必须集中在主线程阶段执行。

# 3 使用约束

## 3.1 生命周期与所有权

- graph 为资源只读；更新必须在写阶段统一执行并 bump 版本。
- result.Handle 的所有权属于 PathStore，业务侧只持有句柄。

## 3.2 容量与预算

- 每请求必须包含 maxExpanded；缺失则拒绝入队/拒绝求解。
- 若启用 corridor 限制，必须在求解前确认 LoadedView 已就绪，否则返回 NotReady。

## 3.3 失败策略

- 任何超预算必须显式返回 BudgetExceeded，不允许静默降级为错误路径。

# 4 代码入口

## 4.1 接口定义位置

- `src/Core/Navigation/GraphCore/NodeGraph.cs`

## 4.2 关键实现位置

- `src/Core/Navigation/GraphCore/NodeGraphPathService.cs`
- `src/Core/Navigation/GraphCore/NodeGraphPathScratch.cs`

## 4.3 相关测试与对齐文档

- `docs/04_游戏逻辑/10_导航寻路/05_对齐报告/01_导航寻路_对齐报告.md`

