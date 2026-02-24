---
文档类型: 架构设计
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 核心引擎 - 总架构 - GameEngine
状态: 草案
依赖文档:
  - docs/00_文档总览/规范/11_架构设计.md
---

# GameEngine 架构设计

# 1 设计概述

## 1.1 本文档定义

本文档定义 `GameEngine` 的生命周期、装配职责、扩展点与边界约束，用于避免上层系统越权访问或引入不可控副作用。

## 1.2 设计目标

1. 生命周期清晰：初始化、加载、Tick、切图、退出的职责边界稳定。  
2. 系统执行可控：系统分组与推进机制可预算、可观测。  
3. 可扩展：Mod/Trigger 通过显式注入点扩展，不直接耦合引擎内部细节。  

## 1.3 设计思路

1. 引擎只做“装配与调度”，业务只做“策略与内容”。  
2. 全局共享通过 `GlobalContext` + `ContextKeys` 显式暴露。  
3. 关键机制（Phase/TimeSlice/运行控制）分别建立 SSOT 文档并通过引用串联。  

# 2 功能总览

## 2.1 术语表

| 术语 | 定义 |
|---|---|
| GlobalContext | 引擎全局上下文字典（显式注入） |
| ContextKeys | 全局上下文键集合 |
| SystemGroup | 引擎系统分组（Phase） |
| Pacemaker | 固定步供给与预算推进策略 |

## 2.2 功能导图

```
Initialize
  → Load Core Config
  → Load Mods (VFS + triggers)
  → Assemble services + systems

LoadMap(mapId)
  → Build world entities
  → Fire MapLoaded event

Tick(dt)
  → Pacemaker produces fixed ticks
  → CooperativeSimulation steps systems by SystemGroup order
```

## 2.3 架构图

```
┌─────────────────────────────┐
│ GameEngine                   │  lifecycle + assembly + scheduling
└──────────────┬──────────────┘
               │ provides
               ▼
┌─────────────────────────────┐
│ GlobalContext / Registries   │  explicit injection points
└──────────────┬──────────────┘
               │ drives
               ▼
┌─────────────────────────────┐
│ Pacemaker + CooperativeSim   │  fixed ticks + phase stepping
└─────────────────────────────┘
```

## 2.4 关联依赖

- 系统分组与执行时序：`docs/02_核心引擎/02_系统分组与执行时序/00_总览.md`  
- TimeSlice 与分帧策略：`docs/02_核心引擎/03_TimeSlice与分帧策略/00_总览.md`  
- 运行控制（run-by-step/run-until）：`docs/02_核心引擎/04_运行控制与调试/00_总览.md`  

# 3 业务设计

## 3.1 业务用例与边界

用例：

- 游戏运行：按固定步推进系统，保持确定性与预算可控。  
- 工具/测试：支持暂停与单步推进，支持“run-until”类自动推进到条件满足。  

边界：

- GameEngine 不包含业务逻辑；业务系统通过注册系统与写入上下文实现接入。  
- 任何可选行为必须配置化并强校验，禁止静默 fallback。  

## 3.2 业务主流程

```
RenderFrame(dt)
  → Engine.Update(dt)
    → Pacemaker.Update(dt, cooperativeSimulation, budgetMs, maxSlices)
      → CooperativeSimulation.Step(fixedDt, remainingMs)
        → SystemGroup phases executed in order
```

## 3.3 关键场景与异常分支

- Trigger 执行异常：不得静默吞掉；必须可观测并中止/上抛到统一错误处理口径。  
- 预算耗尽/切片超限：触发 BudgetFuse 并 reset，避免半状态。  

# 4 数据模型

## 4.1 概念模型

- `GameEngine`：生命周期与调度入口  
- `GlobalContext`：注入容器  
- `SystemGroup → Systems[]`：分组与稳定顺序  

## 4.2 数据结构与不变量

1. `SystemGroup` 顺序为单一真源，不允许运行态重排。  
2. 全局能力入口必须通过 `ContextKeys` 提供稳定键。  
3. 同一能力域只能有一个 SSOT 文档，避免平行叙事分叉。  

## 4.3 生命周期/状态机

```
Created → Initialized → Running
                 │
                 ├─ LoadMap → Running
                 └─ Shutdown → Stopped
```

# 5 落地方式

## 5.1 模块划分与职责

- 装配：创建 world、注册系统、注入全局上下文。  
- 运行：在 `Update` 中用 Pacemaker 产生 fixed tick 并驱动 cooperative simulation。  
- 扩展：Mod/Trigger 写入上下文与注册系统，不直接访问引擎内部对象图。  

## 5.2 关键接口与契约

- Pacemaker 接口：`src/Core/Engine/Pacemaker/IPacemaker.cs`  
- cooperative simulation：`src/Core/Engine/Pacemaker/ICooperativeSimulation.cs`  

## 5.3 运行时关键路径与预算点

- 关键路径：`src/Core/Engine/GameEngine.cs` 的 Update → pacemaker → cooperative step。  
- 预算点：`timeBudgetMs` 与 `maxSlicesPerLogicFrame` 控制每帧推进上限。  

# 6 与其他模块的职责切分

## 6.1 切分结论

- 引擎：负责“装配 + 调度 + 预算边界 + 可观测控制器”。  
- 基础服务：提供可复用的接口契约（空间查询、图运行时等）。  
- 业务逻辑：实现策略与内容，但必须服从引擎的时序与预算口径。  

## 6.2 为什么如此

只有保持“引擎调度单一真源”，才能保证 determinism、可测试性与跨平台一致性。

## 6.3 影响范围

- 新增全局能力或控制器必须同步更新 ContextKeys 与 SSOT 文档索引。  

# 7 当前代码现状

## 7.1 现状入口

- 引擎：`src/Core/Engine/GameEngine.cs`
- 全局键：`src/Core/Scripting/ContextKeys.cs`
- Pacemaker：`src/Core/Engine/Pacemaker/`

## 7.2 差距清单

| 设计口径 | 代码现状 | 差距等级 | 风险 | 证据 |
|---|---|---|---|---|
| 扩展点显式注入 | 已通过 GlobalContext/ContextKeys 提供 | 低 | 低 | `src/Core/Scripting/ContextKeys.cs` |
| 调度与预算口径固化 | 已由 Pacemaker/CooperativeSim 固化 | 低 | 低 | `src/Core/Engine/Pacemaker/` |
| 文档与代码对齐 | 需持续维护 SSOT 与对齐报告 | 中 | 中 | `docs/00_文档总览/` |

## 7.3 迁移策略与风险

- 对于历史文档或目录结构变更，必须使用“兼容入口”保留旧路径，并在总览索引中登记。  

# 8 验收条款

1. 引擎启动流程可重复、无隐式依赖（可测试）。  
2. 系统分组与执行顺序可定位且稳定（可测试）。  
3. LoadMap 后必触发 MapLoaded，且异常不允许静默吞掉（可测试）。  
