---
文档类型: 架构设计
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 核心引擎 - TimeSlice - 多频率与分帧策略
状态: 草案
依赖文档:
  - docs/00_文档总览/规范/11_架构设计.md
  - docs/02_核心引擎/02_系统分组与执行时序/01_系统分组与执行时序_架构设计.md
---

# TimeSlice 与分帧策略 架构设计

# 1 设计概述

## 1.1 本文档定义

本文档定义引擎侧 TimeSlice 的基本模型，以及“目标 Hz / 分帧分桶 / workUnits 队列”三种多频率策略如何在不破坏确定性的前提下落地。

## 1.2 设计目标

1. 确定性：同一输入序列在不同切片划分下结果一致。  
2. 可预算：高成本计算可在多个渲染帧内完成同一 FixedTick。  
3. 可退化：超预算时必须 fail-fast 或显式降级，不允许静默 fallback。  

## 1.3 设计思路

1. 裁决真源是离散 tick（IClock），禁止用 wall-clock 或浮点 dt 作为裁决真源。  
2. 暂停/恢复由 cooperative simulation 统一承载；系统只暴露 cursor 与 reset。  
3. 多频率策略只影响“是否在本 tick 推进/推进多少工作单元”，不改变裁决边界。  

# 2 功能总览

## 2.1 术语表

| 术语 | 含义 |
|---|---|
| TimeSlice | 在同一 FixedTick 内暂停/恢复推进 |
| targetHz | 目标更新频率（可为 0Hz） |
| Bucketing | 按稳定键取模的分帧分桶 |
| workUnits | 最小工作单元计量（用于队列/迭代推进） |
| BudgetFuse | 最大切片数超限后的熔断 reset |

## 2.2 功能导图

```
FixedTick
  │
  ├─ targetHz distributor → steps this tick
  ├─ bucketing → process bucket i only
  └─ workUnits queue → run N units under budget (time-sliced)
```

## 2.3 架构图

```
┌─────────────────────────────┐
│ CooperativeSimulation        │  暂停/恢复承载点
└──────────────┬──────────────┘
               │ calls
               ▼
┌─────────────────────────────┐
│ ITimeSlicedSystem            │  cursor + ResetSlice
└──────────────┬──────────────┘
               │ uses
               ▼
┌─────────────────────────────┐
│ UpdateRatePolicy             │  targetHz / bucketing / workUnits
└─────────────────────────────┘
```

## 2.4 关联依赖

- cooperative 与预算：`src/Core/Engine/Pacemaker/PhaseOrderedCooperativeSimulation.cs`  
- pacemaker 外层预算：`src/Core/Engine/Pacemaker/IPacemaker.cs`  
- 目标 Hz 分发参考实现：  
  - `src/Core/Engine/Physics2D/DiscreteRateTickDistributor.cs`  
  - `src/Core/Engine/Physics2D/Physics2DTickPolicy.cs`  

# 3 业务设计

## 3.1 业务用例与边界

用例：

- 避障/steering：高频、强预算，允许降频但不可破坏裁决一致性。  
- 寻路/flow：高成本、迭代型，必须可暂停/恢复并可观测进度。  

边界：

- TimeSlice 不定义业务算法，只定义预算推进模型与约束。  
- 不为历史行为保留静默兼容分支；迁移必须通过显式策略与验收用例完成。  

## 3.2 业务主流程

```
RenderFrame(dt)
  │ budgetMs + maxSlices
  ▼
Pacemaker → CooperativeSimulation.Step(fixedDt, remainingMs)
  │
  └─ ITimeSlicedSystem.UpdateSlice(dt, remainingMs)  (pause/resume)
```

## 3.3 关键场景与异常分支

- 0Hz：子系统停止推进，但仍保留可观测状态（用于裁剪/调试）。  
- 超预算：触发 BudgetFuse，reset 并记录可观测原因。  

# 4 数据模型

## 4.1 概念模型

- `UpdateRatePolicy`：描述 targetHz、bucketing、workUnits 上限等参数  
- `StableKey`：分桶与 tie-break 的稳定键  
- `cursor`：time-sliced 系统内部推进游标  

## 4.2 数据结构与不变量

1. 分桶键必须稳定：只依赖 stable id，不依赖容器遍历顺序。  
2. workUnits 必须可计量：每个最小单元定义稳定。  
3. reset 必须幂等：BudgetFuse 后可回到干净态。  

## 4.3 生命周期/状态机

```
Ready → Running → (Paused ↔ Running) → Done
          │
          └─ Fuse → Reset → Ready
```

# 5 落地方式

## 5.1 模块划分与职责

- 引擎：提供暂停/恢复与预算边界（cooperative + pacemaker）。  
- 系统：提供 cursor/workUnits，并实现 ResetSlice。  
- 策略：提供 targetHz/bucketing 参数与切换机制（可版本化）。  

## 5.2 关键接口与契约

- `ITimeSlicedSystem.UpdateSlice/ResetSlice`：系统必须实现  
- `IClock`：裁决域推进必须由权威系统显式推进  

## 5.3 运行时关键路径与预算点

- 预算点：每次切片推进消耗预算；超出 maxSlices 触发 fuse。  
- 诊断点：必须输出 dropped/early-exit reason/workUnits 进度等可观测信号。  

# 6 与其他模块的职责切分

## 6.1 切分结论

- 引擎控制预算与暂停/恢复；业务控制工作单元定义与退化策略。  

## 6.2 为什么如此

只有把暂停/恢复统一放在引擎侧，才能保证不同业务系统组合时仍维持一致性与可预测耗时。

## 6.3 影响范围

- 若业务系统把 dt 当裁决真源，会破坏回放一致性。  
- 若业务系统在 dropped 发生时静默继续，会导致不可控的战术差异。  

# 7 当前代码现状

## 7.1 现状入口

- time-slice 承载：`src/Core/Engine/Pacemaker/PhaseOrderedCooperativeSimulation.cs`  
- 外层预算：`src/Core/Engine/Pacemaker/IPacemaker.cs`  
- targetHz 分发参考：`src/Core/Engine/Physics2D/DiscreteRateTickDistributor.cs`  

## 7.2 差距清单

| 设计口径 | 代码现状 | 差异等级 | 风险 | 证据 |
|---|---|---|---|---|
| 统一暂停/恢复 | 已由 cooperative 承载 | 低 | 低 | `src/Core/Engine/Pacemaker/PhaseOrderedCooperativeSimulation.cs` |
| targetHz 分发 | Physics2D 已实现 | 低 | 低 | `src/Core/Engine/Physics2D/DiscreteRateTickDistributor.cs` |
| 业务侧 workUnits 标准化 | 尚未统一 | 中 | 中 | `src/Core/Gameplay/GAS/Systems/AbilityTaskSystem.cs` |

## 7.3 迁移策略与风险

- 对于寻路/flow/避障等高成本模块，统一采用 cursor/workUnits 形态，并在触发 fuse 时可恢复。  

# 8 验收条款

1. 对任意切片划分，同一输入序列输出一致（回放可复现）。  
2. 对任意 targetHz（含 0Hz），推进行为确定，且不超过每 tick 的最大步数约束。  
3. 分帧分桶只依赖 stable key，不依赖容器遍历顺序。  
4. BudgetFuse 后系统可 reset 到干净态并继续推进。  
