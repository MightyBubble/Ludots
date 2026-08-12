# 图分层：Flow / Script 与行为调度

## 1. 概述

Ludots 的图能力分成三层，对标 Paradox **FlowCanvas（细流程）+ NodeCanvas（粗行为）**：

1. **L0 发动机**：一套指令、一套登记表、一套 handler 执行器（含一次跑完 / 按拍切片）
2. **L1 流程图方言**：`Script`、`Effect`、`Score`、`Query`、`Validation`、`Derived`
3. **L2 行为调度**：行为树、**分层状态机（HFSM）**、关卡触发——自己管粗结构，叶子调用 L1 图（学 Animator 的转移索引/校验思路，但不复用表现层类型）

本页是分层合同的文档 SSOT；实现以 `GraphKind`、`GasGraphOpHandlerTable`、`GraphKindOperationPolicy` 为准。

## 2. 结构

```text
L2 BehaviorTree / HFSM / LevelDirector ← 粗节点拓扑（Core runtime 已落地）
        │ BT ScriptSlice(GraphId) / HFSM GraphProgramHfsmHost / Level RunScript
L1 全 Kind 作者 SSOT ← GraphControlFlowDocument（controlEdges + valueEdges）
        │ Kind → GraphProgramAuthoringFrontDoor → GraphControlFlowCompiler
L0 GraphInstruction + handler table + Execute / ExecuteSlice
```

说明（#861）：`GAS/graphs.json` **唯一加载前门**是 `GraphProgramAuthoringFrontDoor`——按 `kind` 校验作者 schema，再进 `GraphControlFlowCompiler`。正式资产必须写 `controlEdges` / `valueEdges`；`nodes[].next` 在加载路径硬拒（不得再按「有没有 controlEdges」猜编译器）。节点白名单按 Kind 过滤（Script / Query / Effect·Score·Validation·Derived 线性方言）。Query 列表流仍用 `list` 显式连接。旧 `GraphCompiler`（next-chain）仅保留给对照/研究测试，不是生产真相。`Execute`/`ExecuteSlice` 均要求调用方提供 CallStack。BT 条件 Script 前由 `IBehaviorTreeSensorFeed` 写入 I[0]。

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

### Script 作者糖（编译期降级）

作者节点名 SSOT：`GraphAuthoringSugar`（非 `GraphNodeOp`）。仅 **Script** ControlFlow 文档可用；Query / Effect / Score / Validation / Derived 使用必须失败关闭。运行时仍是同一套 L0 handler 表，不新增 While/Switch opcode。

| 节点 | 端口 | 降级为 L0 | Kind |
|------|------|-----------|------|
| `BranchBool` | `condition`（bool 值边）；`true` / `false`（控制边） | `JumpIfFalse` + `Jump` | Script only |
| `SwitchInt` | `selector`（int 值边）；`case:{N}` / `default`（控制边） | 每臂 `ConstInt` + `CompareEqInt` + `JumpIfFalse` + `Jump`，再 `Jump(default)` | Script only |
| `Wait` | `next`（控制边） | 作者别名 → `Yield` | Script only |
| `While` | `condition`（bool）；`body` / `next`（控制边） | `JumpIfFalse(cond)→next` + `Jump→body`（回边由作者边闭合） | Script only |
| `Until` | `condition`（bool）；`body` / `next`（控制边） | `JumpIfFalse(cond)→body` + `Jump→next`（条件为真时退出） | Script only |

步数硬顶：`GraphVmLimits.MaxInstructionsPerExecution`（失控循环失败关闭，禁止静默截断当成功）。

与 [#861](https://github.com/MightyBubble/Ludots/issues/861) / PR #863（`GraphAuthoringKindPolicy` Kind 矩阵）的合并约定：糖必须保持 **Script-only**；扩展线性 Kind 前门时不得把上述糖名放进 Effect/Score 白名单；两线改 `GraphControlFlowCompiler.ParseOps` 时以本表 + `GraphAuthoringSugar` 为名册 SSOT。

### Query ControlFlow

- 列表流（`Query*` / `Relationship*` 过滤与聚合）必须用 `valueEdges` 的 `list` 显式连接；控制边 `next` 不隐含 TargetList。
- 实体输入用 `source`；区间过滤用 `min` / `max`；`QueryFilterTeam` 的队伍来源必须二选一：节点字段 `teamId`，或 `teamId` int value pin。
- 标量 / 实体结果绑 Summary；当前 TargetList 结果绑 EntityCollection（`collectionKey` 必填）。

### L2 HFSM 绑定合同
- **转移上配条件**：`ConditionGraphId`（Script/Validation）+ 可选快速 builtin（如 Stimulus）
- **状态节点上配动作**：`OnEnterGraphId` / `OnTickGraphId` / `OnExitGraphId`（Script）
- **Func lib**：`GraphFunctionCatalog` 名字 → 已登记 L1 图 id（Script/Validation/Score）
- **Macro**：不支持编译期文本宏；复用走 Func lib + `InvokeScript` / Script 内 `Call`
- 拓扑仍不编进 `GraphNodeOp`；禁止平行 VM

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
