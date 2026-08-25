# 图分层：Flow / Script 与行为调度

## 1. 概述

Ludots 的图能力分成三层，对标 Paradox **FlowCanvas（细流程）+ NodeCanvas（粗行为）**：

1. **L0 发动机**：一套指令、一套登记表、一套 handler 执行器（含一次跑完 / 按拍切片）
2. **L1 流程图方言**：`Script`、`Effect`、`Score`、`Query`、`Validation`、`Derived`
3. **L2 行为调度**：行为树、**分层状态机（HFSM）**、地图触发器图（TriggerGraph，地图级反应式关卡方言，替代已退役的 LevelDirector）——自己管粗结构，叶子/事件入口调用 L1 图（学 Animator 的转移索引/校验思路，但不复用表现层类型）

本页是分层合同的文档 SSOT；实现以 `GraphKind`、`GasGraphOpHandlerTable`、`GraphKindOperationPolicy` 为准。

## 2. 结构

```text
L2 BehaviorTree / HFSM / TriggerGraph ← 粗节点拓扑（Core runtime 已落地）
        │ ActionLib：GAS/action_lib.json → GraphActionCatalog（叶子/切片宿主解析）
        │ 只引用 ActionLib / GraphProgramRegistry GraphId（禁止旁路 Dictionary/内嵌 Compile）
L1 全 Kind 作者 SSOT ← GraphControlFlowDocument（controlEdges + valueEdges）
        │ Kind → GraphProgramAuthoringFrontDoor → GraphControlFlowCompiler
        │ Func Lib：GAS/func_lib.json → GraphFunctionCatalog（InvokeScript.functionName，pure）
L0 GraphInstruction + handler table + Execute / ExecuteSlice
```


说明（#861）：`GAS/graphs.json` **唯一加载前门**是 `GraphProgramAuthoringFrontDoor`——按 `kind` 校验作者 schema，再进 `GraphControlFlowCompiler`。正式资产必须写 `controlEdges` / `valueEdges`；`nodes[].next` 在加载路径硬拒（不得再按「有没有 controlEdges」猜编译器）。节点白名单按 Kind 过滤（Script / Query / Effect·Score·Validation·Derived 线性方言）。Query 列表流仍用 `list` 显式连接。旧 `GraphCompiler`（next-chain）已删除；测试与生产均走 FrontDoor/ControlFlow。`Execute`/`ExecuteSlice` 均要求调用方提供 CallStack。BT 条件 Script 前由 `IBehaviorTreeSensorFeed` 写入 I[0]。

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
| TriggerGraph | 触发器图：挂载域 map/entity（实体模板 TriggerGraphs/地图 TriggerGraphs）、事件入口表（entries[] + filters）、挂载（MapConfig.TriggerGraphs）、入口起 PC 分发、地图变量与面板算子 | 允许（宿主在思考波上逐拍续跑；旗舰样例见「夜袭三波」展厅） |
| Effect | 技能阶段 | **禁止** |
| Score | 效用打分 | 禁止 |
| Validation / Query / Derived | 既有专项 | 禁止 |

跨图复用：`InvokeScript`（本切片只允许目标 Script **不含 Yield**）。`InvokeScript` 图在 `GraphProgramRegistry.Register` / `ReplaceProgram` 时必须是有向无环的；自调用或 A→B→A 以 `GAS.GRAPH.ERR.InvokeCycle` 失败关闭。运行期另有 `GraphVmLimits.MaxInvokeDepth`，且整棵调用树共享 `MaxInstructionsPerExecution`；超限抛错，禁止静默截断，也禁止把栈溢出当成可捕获错误。

### 作者糖（编译期降级）

作者节点名 SSOT：`GraphAuthoringSugar`（非 `GraphNodeOp`）。`BranchBool` 可用于 **Script / Effect**（`IsBranchBoolAuthorable`）；`SwitchInt` / `Wait` / `While` / `Until` 仅 **Script**。Query / Score / Validation / Derived 使用上述糖名必须失败关闭。运行时仍是同一套 L0 handler 表，不新增 While/Switch opcode。

BT 组合糖（`BtSequence` / `BtSelector` / `BtDecorator`，仅 Script）同属这张名册：整棵行为树在编译期内联成**单个 Script 程序**——组合节点降级为 `Call`/`Return` + `CompareEqInt` + `JumpIfFalse`（子状态走共享 int 寄存器，0=Failure / 1=Success / 2=Running，见 `GraphBtStatusCodes`），叶子链终端按产出类型降级为状态尾声（Int→`MoveInt`、Bool→分支写 0/1、Void→恒 1），只有树根出口 `HaltReturnInt`；嵌套深度受 `MaxCallStackDepth`（16，与 BT `MaxStackDepth` 对齐）静态与运行时双重失败关闭。`GraphBehaviorTreeHost` 只做 per-agent 帧驻留与 think wave `ExecuteSlice` 续跑（Yield 叶跨波恢复天然复用 callStack），不自带树遍历；旧 C# 解释器（`BehaviorTreeWorld`）保留为旧数据路径，图路径不得调用其遍历/PopAndPropagate。

| 节点 | 端口 | 降级为 L0 | Kind |
|------|------|-----------|------|
| `BranchBool` | `condition`（bool 值边）；`true` / `false`（控制边） | `JumpIfFalse` + `Jump` | Script, Effect |
| `SwitchInt` | `selector`（int 值边）；`case:{N}` / `default`（控制边） | 每臂 `ConstInt` + `CompareEqInt` + `JumpIfFalse` + `Jump`，再 `Jump(default)` | Script only |
| `Wait` | `next`（控制边） | 作者别名 → `Yield` | Script only |
| `While` | `condition`（bool）；`body` / `next`（控制边） | `JumpIfFalse(cond)→next` + `Jump→body`（回边由作者边闭合） | Script only |
| `Until` | `condition`（bool）；`body` / `next`（控制边） | `JumpIfFalse(cond)→body` + `Jump→next`（条件为真时退出） | Script only |
| `BtSequence` | `child:{N}`（控制边，按序） | 每子 `Call` + 失败/Running 检查（`ConstInt`+`CompareEqInt`+`JumpIfFalse`+`Jump`），出口写状态寄存器后 `Return`（根则 `HaltReturnInt`） | Script only |
| `BtSelector` | `child:{N}` | 对偶：成功短路；全失败→Failure；Running 同 Sequence | Script only |
| `BtDecorator` | `child:0`；字段 `decoratorKind`（`inverter`/`forceSuccess`/`forceFailure`） | `Call` 子 + 状态改写检查；Running 原样透传 | Script only |

步数硬顶：`GraphVmLimits.MaxInstructionsPerExecution`（失控循环失败关闭，禁止静默截断当成功）。

与 [#861](https://github.com/MightyBubble/Ludots/issues/861) / PR #863（`GraphAuthoringKindPolicy` Kind 矩阵）的合并约定：`SwitchInt` / `Wait` / `While` / `Until` 保持 **Script-only**；`BranchBool` 另允许 Effect。扩展 Score / Validation / Derived 前门时不得把糖名放进白名单；两线改 `GraphControlFlowCompiler.ParseOps` 时以本表 + `GraphAuthoringSugar` 为名册 SSOT。

### Query ControlFlow

- 列表流（`Query*` / `Relationship*` 过滤与聚合）必须用 `valueEdges` 的 `list` 显式连接；控制边 `next` 不隐含 TargetList。
- 实体输入用 `source`；区间过滤用 `min` / `max`；`QueryFilterTeam` 的队伍来源必须二选一：节点字段 `teamId`，或 `teamId` int value pin。
- 标量 / 实体结果绑 Summary；当前 TargetList 结果绑 EntityCollection（`collectionKey` 必填）。
- 空间查询容量：节点字段 `queryCapacityPolicy` 为 `RequireComplete` 或 `AllowTruncated`；后者必须同时声明 `droppedOutput`（未声明则失败关闭）。

### FrontDoor 作者面（已恢复）

- Kind 白名单：`GraphOpDescriptorTable`（kinds × ports × operand roles）是唯一数据源；前门矩阵、`GetLinearOutputType` / `GetQueryOutputType`、端口白名单、覆盖表 `authorableKinds` 由它投影。Score / Validation 只投影 Pure 节点；Derived 额外投影 `WriteSelfAttribute`；Yield 只给 Script。覆盖登记：`assets/GAS/graph_node_op_coverage.registry.json`。
- Tag / 显示作者糖（`GraphNodeOpParser`）：`ReadGameplayTag` → `SelectTagInMask`，`LookupTagDisplayText` → `LookupTagDisplayToken`（亦可直接写 L0 名）。字段：`displayTable`（必填）、`tagSelectPolicy`（`RequireOne` \| `AllowNone` \| `LowestId`，默认 `RequireOne`，仅 Select）。值边：`HasTag` / `SelectTagInMask` 用 `source`；`CompareEqEntity` 用 `a` / `b`；`LookupTagDisplayToken` 用 `a`（tagId）。登记与 Runtime：`TagDisplayTableRegistry`、`IGraphRuntimeApi.SelectEffectiveTagInMask` / `LookupTagDisplayToken`。详见 [Tag 显示查表](tag-display-lookup.md)。
- Effect 线性复用：`InvokeScript.functionName` 只解析 FuncLib（禁 `graphId`、禁 ActionLib 名）。证据：`GraphEffectAuthoringExpressivenessTests`。

### L2 HFSM 绑定合同
- **转移上配条件**：`ConditionGraphId`（Script/Validation）+ 可选快速 builtin（如 Stimulus）
- **状态节点上配动作**：`OnEnterGraphId` / `OnTickGraphId` / `OnExitGraphId`（Script）
- **Func lib（正式）**：`GAS/func_lib.json`（`name` / `graph` / `kind`）→ `GraphFunctionCatalogLoader` 在图登记后加载；作者节点 `InvokeScript.functionName` 编译期进符号表，`PatchFuncLib` 在 ActionLib 加载前解析为 GraphId。未登记到 FuncLib 的名字失败关闭，ActionLib 名不得通过 `functionName` 进入 Effect / 线性 Kind。引擎服务键：`CoreServiceKeys.GraphFunctionCatalog`。
- **加载顺序**：graphs 注册 → func_lib 加载 → FuncLib Invoke patch → action_lib 加载（详见 [FuncLib / ActionLib 合同](graph-funclib-actionlib-contract.md)）。
- **Macro**：不支持编译期文本宏；复用只走 Func lib + `InvokeScript` / Script 内 `Call`
- **L2 数据作者面**：`AI/behavior_trees.json` + `AI/hfsm.json` → `GraphBehaviorDefinitionLoader`；叶子与生命周期绑定只写 ActionLib 名，禁止用 Core 工厂参数代替数据作者面。
- **HFSM Yield**：禁止。OnTick 是 think-wave 节拍；含 Yield 的 `Hfsm` 条目在 ActionLib 加载期失败关闭。
- **行为入口**：L2 叶子 / 切片宿主解析 ActionLib 名或已登记 GraphId；勿使用已标 obsolete 的 `GraphRegistryScriptResolver.RequireId(string)` 字符串旁路。
- **FuncLib / ActionLib 合同**：纯函数库与可挂起动作库拆分、Effect Duration/Period 与阶段表达力——见 [FuncLib / ActionLib 合同](graph-funclib-actionlib-contract.md)。
- 拓扑合同（BT-1 修订）：组合语义（BT 的 Sequence/Selector/Decorator）以作者面糖在编译期内联进图指令（SwitchInt 同款，糖永不成为 opcode）；L2 宿主（`GraphBehaviorTreeHost`）只负责 per-agent 帧与 think wave 驱动，不得自带第二套图 VM 或树遍历解释器。C# `BehaviorTreeWorld` 保留为旧 JSON 树数据路径。一期边界：Parallel 不支持（单 cursor 单 pc，显式 `InvalidOperationException`/文档记边界）；子树跨图复用（InvokeGraph 挂子树）等 BT-2；showcase 迁移（arena 真图化）另开活。

## 4. 场景

- 技能复用一段通用「结算脚本」→ Effect/`InvokeScript` → Script
- 角色 AI 行为树整树写成 Script 图（`BtSequence`/`BtSelector`/`BtDecorator` 糖 + 真实 op 叶子），`GraphBehaviorTreeHost` 按 think wave 逐拍 `ExecuteSlice`；叶子内 `Yield` 跨拍恢复
- 角色 AI 行为树叶子「巡逻一步」→ BT scheduler → Script（可 Yield 跨拍；旧 JSON 树数据路径保留）
- 关卡流「进圈开袭、清场过波、Boss 阵亡翻阶段」→ TriggerGraph（MapConfig.TriggerGraphs 挂载，思考波续跑）

## 5. 边界

- 禁止平行 `GraphVmOpcode` / 第二执行器
- 操作码空闲段控制流为 430+；不得占用已有 GAS 号段假装「FSM 段」
- Effect 事务不得因 Yield 跨帧悬挂

## 6. UAT

见 composition gate 与 `GraphScript*Tests`（ci-gate）。
