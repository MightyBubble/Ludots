# 图分层：Flow / Script 与行为调度

## 1. 概述

Ludots 的图能力分成三层，对标 Paradox **FlowCanvas（细流程）+ NodeCanvas（粗行为）**：

1. **L0 发动机**：一套指令、一套登记表、一套 handler 执行器（含一次跑完 / 按拍切片）
2. **L1 流程图方言**：`Script`、`Effect`、`Score`、`Query`、`Validation`、`Derived`
3. **L2 行为调度**：行为树、**分层状态机（HFSM）**、关卡触发——自己管粗结构，叶子调用 L1 图（学 Animator 的转移索引/校验思路，但不复用表现层类型）

本页是分层合同的文档 SSOT；实现以 `GraphKind`、`GasGraphOpHandlerTable`、`GraphKindOperationPolicy` 为准。

## 2. 结构

```text
L2 BehaviorTree / Fsm / LevelTrigger   ← 粗节点拓扑（尚未实现 runtime）
        │ InvokeScript / Score / Validation
L1 Script / Effect / Score / …         ← 细节点流程（GraphInstruction）
        │
L0 GraphInstruction + handler table + Execute / ExecuteSlice
```

## 3. 详情

### L0
- 指令格式：`GraphInstruction`
- 登记：`GraphProgramRegistry`（可附 source map）
- 执行：`GasGraphOpHandlerTable.Execute`（跑完）/ `ExecuteSlice`（可暂停）
- 改世界：只通过 `IGraphRuntimeApi`；技能事务仍在 effect 生命周期

### L1
| Kind | 用途 | Yield |
|------|------|-------|
| Script | 可复用流程函数 | 允许 |
| Effect | 技能阶段 | **禁止** |
| Score | 效用打分 | 禁止 |
| Validation / Query / Derived | 既有专项 | 禁止 |

跨图复用：`InvokeScript`（本切片只允许目标 Script **不含 Yield**）。

### L2（合同，本切片不实现）
- 行为树 Sequence/Selector、状态机 State/Transition、关卡触发器 **不**编进 `GraphNodeOp`
- 叶子：动作 → Script（或技能模板）；条件 → Validation/Script；效用 → Score
- 禁止再造第二套指令虚拟机，禁止把 L2 程序伪装成 `Effect` 登记

## 4. 场景

- 技能复用一段通用「结算脚本」→ Effect/`InvokeScript` → Script
- 角色 AI 行为树叶子「巡逻一步」→ BT scheduler → Script（可 Yield 跨拍）
- 关卡触发「进圈开门」→ LevelTrigger → Script

## 5. 边界

- 禁止平行 `GraphVmOpcode` / 第二执行器
- 操作码空闲段控制流为 430+；不得占用已有 GAS 号段假装「FSM 段」
- Effect 事务不得因 Yield 跨帧悬挂

## 6. UAT

见 composition gate 与 `GraphScript*Tests`（ci-gate）。
