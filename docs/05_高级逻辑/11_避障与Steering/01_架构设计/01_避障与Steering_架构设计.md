---
文档类型: 架构设计
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 高级逻辑 - 避障与Steering - 近场避障与群体引导
状态: 草案
依赖文档:
  - docs/00_文档总览/规范/11_架构设计.md
  - docs/02_核心引擎/02_系统分组与执行时序/01_系统分组与执行时序_架构设计.md
  - docs/02_核心引擎/03_TimeSlice与分帧策略/01_TimeSlice与分帧策略_架构设计.md
  - docs/03_基础服务/06_空间服务/02_接口规范/01_空间查询接口_接口规范.md
  - docs/04_游戏逻辑/10_导航寻路/00_总览.md
---

# 避障与 Steering 架构设计

# 1 设计概述

## 1.1 本文档定义

本文档定义近场避障/steering 的模块边界、主流程、关键状态机与预算点，并给出“超大世界”下的多表示（Representation）组合方式。

## 1.2 设计目标

1. Non‑Cullable 裁决一致：近场裁决不受镜头/裁剪影响，可回放。  
2. Cullable 可承载：表现级群体/steering 允许降频/分桶/代理。  
3. 输出契约统一：所有上游输出收敛为期望向量，由单一推进器消费。  
4. 可预算与可退化：预算不足时可 time-slice、可熔断、可显式降级。  

## 1.3 设计思路

1. 以空间服务输出为候选真源：固定容量 Span + dropped 可观测。  
2. 以离散 tick 为裁决真源：targetHz/bucketing/workUnits 只改变推进节奏，不改变裁决边界。  
3. 以多表示分层解决冲突：裁决级不可裁剪，表现级可裁剪。  

# 2 功能总览

## 2.1 术语表

| 术语 | 定义 | 说明 |
|---|---|---|
| Non‑Cullable | 不可裁剪表示 | 裁决级，必须持续推进 |
| Cullable | 可裁剪表示 | 表现级，允许降频/代理 |
| DesiredVelocity | 期望速度向量 | 统一输出契约 |
| Near‑Field Resolve | 近场修正 | Sonar/ORCA 类求解器语义 |
| Flow‑Field | 场引导 | 迭代生成场，实体采样 |

## 2.2 功能导图

```
Macro Path (导航寻路)
   │ corridor/target
   ▼
DesiredVelocity (coarse)
   │
   ├─ Near‑Field Resolve (Non‑Cullable)  ← SpatialQuery candidates + K-selection
   │          │ dropped → explicit degrade/fuse
   │          ▼
   │     DesiredVelocity (safe)
   │
   └─ Micro Crowd/Steering (Cullable)    ← flow-field / steering pipeline
              ▼
         DesiredVelocity (pretty)
   ▼
Integrate / Physics2D input
```

## 2.3 架构图

```
┌──────────────────────────┐
│ Navigation (GraphWorld)   │  宏观走廊/目标
└─────────────┬────────────┘
              ▼
┌──────────────────────────┐
│ Avoid/Steering Pipeline    │  统一输出契约：DesiredVelocity
└──────┬───────────┬───────┘
       │           │
       ▼           ▼
 SpatialQuery      FlowField / Steering (Cullable)
 (candidates)      (optional)
       │
       ▼
 Integrate / Physics2D
```

## 2.4 关联依赖

- 执行时序与预算：`docs/02_核心引擎/02_系统分组与执行时序/01_系统分组与执行时序_架构设计.md`  
- 多频率与分帧：`docs/02_核心引擎/03_TimeSlice与分帧策略/01_TimeSlice与分帧策略_架构设计.md`  
- 空间查询契约：`docs/03_基础服务/06_空间服务/02_接口规范/01_空间查询接口_接口规范.md`  
- 导航主干：`docs/04_游戏逻辑/10_导航寻路/00_总览.md`  

# 3 业务设计

## 3.1 业务用例与边界

用例：

- 单位移动：需要持续的近场安全修正（Non‑Cullable）。  
- 群体表现：允许降频与代理，保证整体方向与密度合理（Cullable）。  

边界：

- 近场避障不负责生成宏观路径；宏观路径由导航模块提供走廊/目标。  
- 本模块不引入业务词；所有行为差异由 token/ID 与配置表达。  

## 3.2 业务主流程

```
InputCollection:
  - spatial index updated
  - build candidates (SpatialQuery)
  - choose K neighbors (stable tie-break)
  - solve near-field (Non‑Cullable)
  - optional: crowd/steering embellishment (Cullable)
  - integrate to motion/physics input
```

## 3.3 关键场景与异常分支

- 高密场景 dropped>0：Non‑Cullable 必须触发熔断或显式降级（见裁决条款）。  
- 未加载 chunk：禁止返回跨 chunk 候选；视为一致性 bug（由空间服务 fail-fast）。  

# 4 数据模型

## 4.1 概念模型

- Representation：Non‑Cullable/Cullable 元数据  
- DesiredVelocity：统一输出向量  
- Neighbors：固定容量邻居集（K 选择结果）  

## 4.2 数据结构与不变量

1. 邻居选择必须固定容量，且 tie-break 使用 stable key。  
2. Non‑Cullable 的降级必须显式且可观测。  
3. Cullable 的降频不得反向影响 Non‑Cullable 的裁决结果。  

## 4.3 生命周期/状态机

```
Idle → AcquireCandidates → SolveNearField → (OptionalMicro) → Integrate → Idle
                     │
                     └─ dropped/fuse → Degrade/Reset → Idle
```

# 5 落地方式

## 5.1 模块划分与职责

- Candidates：调用空间服务生成候选，并进行稳定序 K 选择。  
- Solver：对期望向量做近场修正（算法语义可参考外部插件）。  
- Integrate：统一消费期望向量并更新运动状态或写入 Physics2D 输入。  

## 5.2 关键接口与契约

- 空间查询：`src/Core/Spatial/ISpatialQueryService.cs`  
- 空间后端：`src/Core/Spatial/ISpatialQueryBackend.cs`  

## 5.3 运行时关键路径与预算点

- 候选生成：由空间服务的 cell/chunk 覆盖决定扫描成本。  
- 近场求解：必须以固定 K 邻居为上限，避免 O(N²) 退化。  
- 分帧推进：flow-field/迭代任务必须以 workUnits 计量并可 time-slice。  

# 6 与其他模块的职责切分

## 6.1 切分结论

- 空间服务负责候选生成与稳定序门面；避障模块负责 K 选择与求解。  
- 导航模块负责宏观路径；避障模块只做局部修正。  

## 6.2 为什么如此

把候选生成与稳定序统一到空间服务，才能保证跨模块一致性与可测试性；把局部求解隔离到避障模块，才能按不同算法替换而不破坏契约。

## 6.3 影响范围

- 任何直接绑定物理回调/引擎对象句柄的实现都会破坏跨平台与 determinism。  

# 7 当前代码现状

## 7.1 现状入口

- 空间查询：`src/Core/Spatial/SpatialQueryService.cs`  
- 稳定序与去重：`src/Core/Spatial/SpatialQueryPostProcessor.cs`  
- targetHz 分发参考：`src/Core/Engine/Physics2D/DiscreteRateTickDistributor.cs`  

## 7.2 差距清单

| 设计口径 | 代码现状 | 差距等级 | 风险 | 证据 |
|---|---|---|---|---|
| 统一候选契约 | 已由空间服务提供接口与稳定序 | 低 | 低 | `src/Core/Spatial/ISpatialQueryService.cs` |
| 近场求解器模块 | 未形成统一模块边界 | 中 | 中 | `docs/05_高级逻辑/11_避障与Steering/04_裁决条款/01_更新频率与退化策略_裁决条款.md` |
| flow-field 分帧迭代 | 需按 workUnits 标准化 | 中 | 中 | `docs/02_核心引擎/03_TimeSlice与分帧策略/01_TimeSlice与分帧策略_架构设计.md` |

## 7.3 迁移策略与风险

- 先固化候选集与 dropped 的裁决条款，再引入求解器实现与回归用例。  

# 8 验收条款

1. Non‑Cullable 的结果不受镜头/裁剪影响：同一输入序列回放一致。  
2. dropped>0 触发显式退化或熔断：可观测且可回放。  
3. 邻居选择为固定容量 K，且 tie-break 固化，避免高密场景退化。  
