---
文档类型: 接口规范
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 04_游戏逻辑 - 导航寻路 - PathRequest/PathResult
状态: 草案
---

# PathRequest/PathResult 接口规范

# 1 概述

## 1.1 本文档定义

本文档固化 PathRequest 与 PathResult 的字段口径、单位、预算与确定性排序要求，作为所有请求生产者（AI/GAS/GraphRuntime）与求解系统之间的唯一契约。

## 1.2 术语与约定

- RequestId：请求编号；用于对账/取消/稳定排序。
- Actor：请求者实体引用；用于写回定位。
- GraphId：目标图/层标识。
- PathHandle：路径句柄（Index+Generation）。

## 1.3 关键约束

- 运行态禁止 `string`、托管集合、委托进入请求或结果结构。
- 请求必须携带预算；预算缺失视为 InvalidRequest。
- 结果不携带可变长路径数组；路径本体写入 PathStore 并以 handle 引用。

# 2 核心接口

## 2.1 PathRequest

### 签名

字段伪结构（用于口径说明，不代表最终代码必须同名）：

```csharp
struct PathRequest {
  int RequestId;
  EntityReference Actor;
  int GraphId;
  StartSpec Start;
  GoalSpec  Goal;
  PathMode  Mode;
  PathBudget Budget;
  uint Flags;
  PathPriority Priority;
}
```

### 参数说明

| 参数名 | 类型 | 单位/范围 | 说明 |
|---|---|---|---|
| RequestId | int | >0 | 稳定来源；用于排序与对账 |
| Actor | EntityReference | id+version | 写回定位；禁止裸指针 |
| GraphId | int | >=0 | 多图/多层导航入口 |
| Start | StartSpec | world(cm)/nodeId | 起点；若为 world 必须先投影 |
| Goal | GoalSpec | world(cm)/nodeId | 终点；同上 |
| Mode | enum | AStar/RouteTable/... | 求解模式（可扩展） |
| Budget | PathBudget | 见下 | maxExpanded/corridor 上限等 |
| Flags | uint | bitmask | 全部 ID 化 gate（不允许业务字面量） |
| Priority | enum/int | 固定范围 | 需要与排序规则一起写死 |

### 返回值

无。

### 异常/错误码

InvalidRequest 的典型触发条件（必须拒绝入队）：

- GraphId < 0
- Budget.maxExpanded <= 0
- Start/Goal 未投影且依赖未就绪

### 线程安全性

PathRequest 必须可在多线程生产场景下安全传递（通常为不可变值类型拷贝）。

## 2.2 PathResult

### 签名

```csharp
struct PathResult {
  int RequestId;
  EntityReference Actor;
  PathStatus Status;
  PathHandle Handle;
  float Cost;
  int Expanded;
  int ErrorCode;
}
```

### 参数说明

| 参数名 | 类型 | 单位/范围 | 说明 |
|---|---|---|---|
| RequestId | int | >0 | 必须回传，用于对账 |
| Actor | EntityReference | id+version | 写回定位 |
| Status | enum | Found/NoPath/... | 结果状态 |
| Handle | PathHandle | Index+Gen | Found 时有效 |
| Cost | float | cost-unit | 代价；单位必须写死并与图一致 |
| Expanded | int | >=0 | 扩展节点数（审计与调参） |
| ErrorCode | int/enum | 约定表 | 失败原因细分（可选） |

### 返回值

无。

### 异常/错误码

Status 建议至少包含：

- Found
- NoPath
- BudgetExceeded
- NotReady
- Cancelled
- Error

### 线程安全性

PathResult 必须可跨线程安全传递；写回实体必须在主线程或指定阶段执行。

# 3 使用约束

## 3.1 生命周期与所有权

- PathResult 的 Handle 仅在 PathStore 中有效；写回后由消费系统决定何时释放旧 handle。
- Actor 必须使用可校验引用（id+version）；若失效，结果必须被丢弃并计数。

## 3.2 容量与预算

预算字段（强制）建议包括：

| 字段 | 类型 | 单位/范围 | 说明 |
|---|---|---|---|
| maxExpanded | int | >0 | A* 扩展上限 |
| corridorRadius | int | >=0 | corridor 扩张半径（chunk） |
| maxCorridorChunks | int | >0 | corridor 最大 chunk 数 |
| deadlineTick | int | >=0 | 超时/过期（可选） |

## 3.3 失败策略

- QueueFull：入队失败必须返回明确错误码并计数，不允许扩容。
- NotReady：允许返回 NotReady 并由上层重试（重试策略由上层决定，导航不做隐式重试）。
- BudgetExceeded：必须返回 BudgetExceeded，禁止静默降级为错误路径。

# 4 代码入口

## 4.1 接口定义位置

- `src/Core/Navigation/GraphEcs/GraphPathComponents.cs`

## 4.2 关键实现位置

- `src/Core/Navigation/GraphEcs/GraphPathfindingSystem.cs`
- `src/Core/Navigation/GraphCore/NodeGraphPathService.cs`

## 4.3 相关测试与对齐文档

- `docs/04_游戏逻辑/10_导航寻路/05_对齐报告/01_导航寻路_对齐报告.md`

