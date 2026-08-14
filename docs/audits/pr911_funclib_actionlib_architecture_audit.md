# PR #911 架构审计：FuncLib / ActionLib 拆分与 Effect 表达力

**审计对象：** PR #911（`cursor/funclib-actionlib-impl-45dc`）
**审计 tip：** `20bf1e031`（`docs(architecture): mark FuncLib/ActionLib contract as landed`）
**基线：** PR #895 head `99bdad19c`（本 PR 的 base）
**现在怎样：** [图能力唯一入口](../../gitbook/architecture/graph-capability-status.md)（本页是 #911 当时结论，不是当前进度）
**审计需求（当时）：** [`pr911_funclib_actionlib_audit_handoff.md`](pr911_funclib_actionlib_audit_handoff.md)（随 PR #912 入库）
**合同对照：** `gitbook/architecture/graph-funclib-actionlib-contract.md`、`gitbook/architecture/graph-layering-flow-and-behavior.md`
**前序审计：** [`pr895_graph_infra_and_lsw_architecture_audit.md`](pr895_graph_infra_and_lsw_architecture_audit.md)（#906）

---

## 1. 概述

### 1.1 合并结论

**Verdict：DO NOT MERGE（禁止合入）。**

主体方向是对的：仓库现在真的有两本复用清单——一本"纯算式"（FuncLib）、一本"可以做到一半下一拍接着做的动作"（ActionLib）；技能结算阶段第一次可以分叉、可以调用共用算式；那个按"上一个节点连下一个节点"编译的旧编译器确实被删干净了（`src/` 无残留引用）。

但四类问题必须在合入前处理，它们都属于本仓库的红线（禁止静默失败、SSOT、不许拿放宽门槛当交付）：

| # | 阻断项 | 一句话 |
|---|--------|--------|
| B1 | 复用清单文件丢了当没事 | `action_lib.json` / `func_lib.json` 文件缺失或写成空表时，加载照样成功、清单为空，直到运行时才炸 |
| B2 | 两条注册路径上库边界没关严 | 纯函数可以借"直接写图编号"跳进可挂起的动作图；技能工作台热改还能把已登记的纯函数换成含挂起的图，无人复检 |
| B3 | 性能门槛被无依据放宽 | 实测反而更快（3.55ms vs 基线 3.70ms），门槛却从 15ms 放到 20ms/40ms，测试名还叫「StayUnderFiveMs」 |
| B4 | 删旧编译器时顺手截肢了作者面 | 44 个仍在运行的图节点在唯一前门下已无任何作者路径；三条"必须写清楚才让过"的作者规则连唯一守卫测试一起被删；文档却写"测试与生产均走 FrontDoor/ControlFlow" |

### 1.2 与 #906（#895 审计）的衔接

| #906 结论 | 本轮复核 |
|-----------|----------|
| B1 提交非原子 | **已关闭**：`LiveGasEditPipeline` 已用快照 + 逆序回滚（`LiveGasEditPipeline.cs` L192–L361） |
| B2 可热注册新 Tag | **已关闭**：未知 tag → `EngineRestartRequired`（`LiveGasEditPipeline.cs` L1004–L1005），分类路径不再 `TagRegistry.Register` |
| M1 impact/hit 串扰 | **已关闭**：两个字段分开写入（`EffectTemplateRegistry.cs` L560–L580） |
| M2 Classify 不校验 fieldPath | **已关闭**：`IsHotEditableEffectField` / `IsHotEditableProjectileRefField` 在分类阶段拦截 |
| M3 生产挂假 AI 生成器 | **已关闭**：生产注册 `UnconfiguredAiSkillDraftGenerator`（抛错），假生成器仅测试可用 |
| M4 落盘缺 saveModRoot | **部分关闭**：`saveModRoot` 已解析注入（`LiveSkillWorkbenchModEntry.cs` L89–L100）；映射完备性未在本轮逐项复核 |
| 修复清单 #7「FuncLib：catalog 已声明则缺文件必须失败关闭」 | **仍开着，且被复制**：只补了"目录对象为 null / 目录未声明"两支；"目录已声明但文件不存在 / 空数组"仍静默空表，新写的 ActionLib 加载器照抄了同一形状（见 B1） |
| Debt「线性白名单窄于旧编译器」 | **恶化为能力缺口**：旧编译器已删，窄白名单成为唯一作者面，且守卫测试同步删除（见 B4） |
| Debt「#861 S2–S4 未关单」 | 资产侧无 `nodes[].next`，源码侧 `GraphConfig`/`GraphNodeConfig` 遗留 DTO 仍在；不应宣称 #861 关单 |
| Champion 真机火→冰证据 | 本轮未复核（交接 §5 已列为已知缺口，非本 PR 主交付） |

---

## 2. 结构

```text
1 概述：Verdict / 与 #906 衔接
2 结构（本页）
3 详情：审计方法、阻断项、Major、Minor 与债、合同逐条符合性
4 场景：作者与玩家会撞到什么
5 边界：本审计覆盖与不覆盖
6 UAT：合入前必须成立的验收（Cucumber）
附录 A 证据产物
附录 B 给修复 Agent 的最短提示词
```

---

## 3. 详情

### 3.1 审计方法

1. 读审计需求交接与 `gitbook/contributing/ai-assisted-development.md` 的任务执行决策规范。
2. 精确切分 diff 范围：`git diff pr895 pr911` 共 77 文件，其中 `773b0aa24`（#909 cherry-pick）20 文件属前序 PR 修复，`773b0aa24..20bf1e031` 59 文件为 #911 本体。
3. 对合同 §3/§5/§6 逐条读实现源码（加载器、控制流编译器两个分部类、VM handler、L2 解析、Showcase 绑定、引擎装配）。
4. 构建并运行 GasTests 全量（2286 项）；对本 PR 直接相关的 Graph/AI 子集单独运行（86 项全绿）。
5. 在 `pr895` 基线建独立 worktree，重跑同一批关键测试，逐条区分"本 PR 引入"与"基线既有"。
6. 写 6 条一次性探针测试实测边界行为（跑完即删，未进入交付代码），产物见附录 A。

### 3.2 阻断项

#### B1 — 复用清单缺文件 / 空表静默通过

合同 §5 原文：**"禁止缺文件/缺条目静默空表。"**

实现：两个加载器都只在"目录对象为 null"或"目录未声明该路径"时抛错；`ConfigPipeline` 内部 `TryLoad` 对 `FileNotFoundException` 直接忽略（`ConfigPipeline.cs` L202–L205），因此"目录已声明 + 文件不存在"和"文件内容是 `[]`"两种情况都返回空列表，加载器循环零次、静默注册零条。

实测（探针 P1 / P1b，附录 A）：

```
P1  missing-file load succeeded. ActionCatalog.Count=0
P1b empty-array  load succeeded. ActionCatalog.Count=0
```

后果：ActionLib 清单整体消失时，引擎启动不报错；直到某个行为树叶子第一次要动作、`GraphActionCatalog.Require` 才抛 `Graph action 'bt.patrol' is not registered.`。对作者来说，是"改坏了配置但要等到进关才知道"。

路径：`src/Core/NodeLibraries/GASGraph/Host/GraphActionCatalogLoader.cs` L34–L44、L92–L97；`src/Core/NodeLibraries/GASGraph/Host/GraphFunctionCatalogLoader.cs` L36–L44。

这条同时是 #906 修复清单第 7 项未真正关闭，并被新加载器复制。

#### B2 — 库边界在两条注册路径上没有闭合

合同 §5：**"禁止 FuncLib 条目含 Yield"**；§3.3：**"加载后校验：目标图无 Yield/Wait"**。

**(a) Script 方言仍允许 `InvokeScript.graphId` 直绑图编号。**
线性方言（Effect/Score/Validation/Derived）已正确拒绝 `graphId`、强制 `functionName`（`GraphControlFlowCompiler.Linear.cs` L139–L152），这一半是达标的。但 Script 方言仍保留 `graphId` 分支（`GraphControlFlowCompiler.cs` L1310–L1339），而 FuncLib 的禁 Yield 检查只扫入口图自身指令（`GraphFunctionCatalogLoader.cs` L142–L153，单层）。于是一条 Script 图可以"自己不挂起、但直接跳进一张含挂起的图"，并以 `purity: pure` 通过登记。

实测（探针 P2）：

```
P2 caller compile succeeded=True diagnostics=0
P2 FuncLib load succeeded. Count=1 pure.looking -> graphId 2
```

运行期确有兜底：`GasGraphOpHandlerTable.HandleInvokeScript` 会 `RequireNoYield(childProgram)` 抛错（L659、L697–L707），所以不会静默串味。但按合同这应在加载期失败关闭，而不是在一次技能结算事务的半途抛异常；而且 `graphId` 直绑本身就是绕开两本清单的旁路入口，与"实现时二选一，禁止两套名字并存"（§3.3）冲突。

**(b) 技能工作台热应用不复检纯度。**
`GraphProgramRegistry.ReplaceProgram` 只校验"图编号已存在"与"Kind 不变"（`GraphProgramRegistry.cs` L57–L87），不查该图是否被 FuncLib 登记过、替换后是否含 Yield。Script 方言允许 `Wait`/`Yield`，所以作者完全可以把已登记为纯函数的 `ability.slash` 热换成含挂起的图，两本清单无人复检。#911 是把"FuncLib 禁 Yield"写成合同的 PR，就应当在所有注册路径上守住它，不能只守冷启动加载。

路径：`src/Core/Gameplay/GAS/LiveSkillWorkbench/LiveGasEditPipeline.cs` L204–L221；`src/Core/GraphRuntime/GraphProgramRegistry.cs` L57–L87。

#### B3 — 性能门槛被无依据放宽

改动：`GraphBehaviorArenaAcceptanceTests.cs` L93–L95，avg 门槛 15 → 20ms、p95 门槛 15 → 40ms，注释理由是"allow CI noise after Registry + ActionLib bootstrap"。

同机同轮实测（附录 A）：

| 分支 | 门槛 | 实测 |
|------|------|------|
| 基线 #895 `99bdad19c` | avg<15 / p95<15 | avg=3.702 p95=3.738 max=3.742 over5ms=0 |
| 本 PR `20bf1e031` | avg<20 / p95<40 | avg=3.546 p95=3.595 max=3.668 over5ms=0 |

三点判定：

1. **没有需要被容忍的退化**——本 PR 比基线更快 0.16ms。
2. **理由与被测量对象没有因果关系**——ActionLib 装载发生在 L23，测量循环从 L62 才开始，装载耗时不在任何样本内。
3. **放宽后的门槛把产品预算彻底架空**——方法名仍写 `StayUnderFiveMs`，`over5ms` 计数算出来只打印、不断言（L80–L92），而 p95 上限已是实测值的 11 倍、产品口径 5ms 的 8 倍。

这属于"用调门槛代替交付"，与"NO 静默放过"直接冲突。正确做法是门槛还原（或按实测收紧），并把 `over5ms` 变成断言。

#### B4 — 删除旧编译器时静默截去作者面与守卫

PR 删除 `GraphCompiler.cs`（1207 行）与 `GraphValidator.cs`（219 行）本身是对的，`src/` 已无残留引用，文档也改成"唯一编译器"。问题在于同一批改动顺手删掉了它们承载的作者能力与规则守卫，没有任何一处记录这次缩编：

**(1) 44 个仍在运行的图节点已无作者路径。**
`GraphNodeOp` 有 120 个可执行成员，`GraphControlFlowCompiler{,.Linear,.Query}.cs` 三个分部类完全不提及其中 44 个（清单见附录 A），包括：动态施加效果族（`ApplyEffectDynamic` / `FanOutApplyEffect*` / `FanOutDispatchEffect*`）、关系变更族（`RelationshipEnsureLink` / `SetMetric` / `AddMetric` / `GetMetric` / `SetFlag` …）、`QueryRadius`、吸附族（`SnapToNearestInCollection` / `SnapToNearestGraphEdge`）、Tag 显示查表（`SelectTagInMask` / `LookupTagDisplayToken`）、事件载荷读取、拓扑谓词（`ControlDomainResolve` / `KnowledgeHasProjection`）、浮点 min/max/clamp/div/abs/neg、`ConstBool`、`SendEvent`。

这些节点周边的基建仍然在等它们出现：`GraphProgramSymbolPatcher` 仍为 `SelectTagInMask`、`FanOutDispatchEffect`、`RelationshipSetMetric` 做符号解析（L34、L66、L87–L101）；`UtilityAiGraphSafety` 仍在防它们进 Utility 图（L28–L41）；`gitbook/architecture/tag-display-lookup.md` 仍把 `ReadGameplayTag → LookupTagDisplayText` 写成作者链路。VM 层测试仍在（各 3–10 处引用），所以是"能跑但没法写"。

**(2) 三条失败关闭的作者规则失去唯一守卫。**
`GraphCompilerConfigCoverageTests.cs` 更名为 `GraphControlFlowConfigCoverageTests.cs` 时，除了迁移 4 个测试，还静默删掉 6 个：`SpatialQueryCompiler_RejectsMissingCapacityPolicy`、`SpatialQueryCompiler_RejectsAllowTruncatedWithoutDroppedOutput`、`SpatialQuery_RequireComplete_ThrowsWhenRuntimeDropsTargets`、`SpatialQuery_AllowTruncated_PublishesDroppedCount`、`SnapWithoutValidOutput_DoesNotOverwriteValidationResult`、`SnapWithValidOutput_AllocatesDedicatedBoolRegister`。`NewGraphOpsTests.cs` 另删 3 个：`FanOutDispatchEffectDynamic` 必须写 `payloadPreset`、关系度量必须写显式 `relationshipType`、动态施加效果可编译。

结果：`GAS.GRAPH.ERR.SpatialQueryIncomplete`（查询结果被截断时必须抛错）现在**全仓零测试覆盖**，只剩抛出点 `GasGraphOpHandlerTable.cs` L793；`SnapToNearestInCollection` 现在全仓零测试引用。

**(3) 两项能力在代码注释里被判"暂不可写"，仓库无对应记录。**
`GraphControlFlowCompiler.Linear.cs` L324–L329：`AllowTruncated` + `droppedOutput` 直接报错"not yet authorable on ControlFlow linear kinds"；L586–L588：`TargetListGet` 的 `validOutput` 硬写 scratch 寄存器，注释"not yet on the linear CF matrix"。对应的 `GraphNodeConfig.ValidOutput` / `DroppedOutput` 字段成了没有任何编译器读的死字段。

**(4) 文档反向宣称完成。**
`graph-layering-flow-and-behavior.md` L26 改为"旧 `GraphCompiler`（next-chain）已删除；测试与生产均走 FrontDoor/ControlFlow"，`graph-funclib-actionlib-contract.md` L3 标"状态：已落地"。按合同 §5"禁止平行第二套作者边模型"确实前进了一步，但把"删掉替代品"写成"迁移完成"是不成立的。

处置二选一，必须写清：要么补回作者路径与守卫测试，要么把这次作者面缩编写进合同 + 开跟踪单，并把两处"not yet authorable"从代码注释升级为正式记录。

### 3.3 Major

| ID | 缺陷 | 证据 |
|----|------|------|
| M1 | FuncLib 允许登记 `kind=Score/Validation`，但唯一调用节点 `InvokeScript` 在运行时要求 Script（`RequireKind`）→ 登记即运行时地雷，合同 §3.3 承诺的"Score/Validation 可进 FuncLib"不可交付。探针 P3：登记成功，调用侧报 `Graph program id 1 kind is 'Score', but 'Script' is required.` | `GraphFunctionCatalogLoader.cs` L62–L67；`GasGraphOpHandlerTable.cs` L653 |
| M2 | `GraphActionCatalogLoader` 的 FuncLib 同名去重依赖**可选**构造参数（默认 `null`），默认即 fail-open；只有 `GameEngine` 传了，PR 自带的两个加载器测试都没传。探针 P4：不传时同名条目被接受 | `GraphActionCatalogLoader.cs` L14–L26、L52–L55；`GraphActionCatalogLoaderTests.cs` L42、L121 |
| M3 | 测试 bootstrap 手写了第二套 func_lib/action_lib 加载逻辑（不查 `purity`、不查禁 Yield、不查图是否已注册程序、同名检查手工重写），而**全部** L2 与 Showcase 验收都走它 → 生产加载器在验收路径上零覆盖，资产回归不会被这批测试发现。违反 SSOT/DRY | `GraphRegistryTestBootstrap.cs` L18–L51、L91–L110；`GraphBehaviorSeparatedShowcaseAcceptanceTests.cs`、`ScriptFlowSandboxShowcaseAcceptanceTests.cs`、`BehaviorTreeRuntimeTests.cs`、`FsmRuntimeTests.cs`、`LevelDirectorRuntimeTests.cs` |
| M4 | 合同 §6 六条 Cucumber 场景无 UAT 映射：`gitbook/acceptance/` 只有 `live-skill-workbench-uat.md` 一页，`showcase.registry.json` 五个图行为条目也不引用本合同。其中"技能阶段不能调用 ActionLib"零测试——实测是在符号 patch 阶段失败（探针 P5：`Graph function 'bt.patrol' is not registered.`），而合同写的是"编译必须失败"，措辞与实现不一致；"行为树叶子调用 ActionLib 断点续跑"没有任何含 Yield 的 `bt.*` 资产可验 | `gitbook/architecture/graph-funclib-actionlib-contract.md` §6；`gitbook/acceptance/`；`showcase.registry.json` |
| M5 | ActionLib 的 11 条内容里只有 `script.drinkUntilFull` 真含 Yield，其余 10 条（`bt.*` / `hfsm.*` / `level.phaseAdvance`）是 `ConstInt → HaltReturnInt` 纯图 → 本次"迁移"是按名字前缀搬家，不是按运行时性质划分。加载器也不校验内容性质：ActionLib 从不检查"是否真能挂起"，FuncLib 的 `purity` 只比字符串是否等于 `pure`，不看图里有没有副作用 | `assets/Configs/GAS/{func_lib,action_lib}.json`；`assets/Configs/GAS/graphs.json`；`GraphFunctionCatalogLoader.cs` L55–L60 |

补充说明 M5 的缓解事实：Script 方言的作者白名单本身极窄（仅常量/整数运算/比较/跳转/调用/返回/挂起/`InvokeScript`/`MoveInt`，`GraphControlFlowCompiler.cs` L482–L492），因此 Script 类图**结构上**无法写出 `ApplyEffect` 类副作用——`ability.slash` 标 `pure` 属实。但这是白名单顺带带来的结果，不是 `purity` 字段在把关；一旦 Script 白名单放宽，`purity` 立刻变成一句没有校验的声明。

### 3.4 Minor 与债

| 严重度 | 项 | 路径 |
|--------|----|------|
| Minor | 文档门户仍以旧编译器为真相：`tag-display-lookup.md` 三处把 `GraphCompiler` 写进管线；`docs/prd/12-config-scripting.html`、`docs/tdd/04-pipeline-design.html`、`docs/reference/api-quickref.html`、`docs/diagrams/graph-compiler-flow.svg`、`docs/diagrams/opcode-taxonomy.svg` 仍描述 `GraphValidator → GraphCompiler` | `gitbook/architecture/tag-display-lookup.md` L46/L344/L556；`docs/` 上述文件 |
| Minor | 遗留作者 DTO `GraphConfig` / `GraphNodeConfig` 仍在（PR 只删了 `Next` 字段），带一批无人读取的死字段（`ValidOutput`、`DroppedOutput`、`RadiusCm`、`Inputs`…），构成第二套作者 DTO；生产路径实际反序列化 `GraphControlFlowDocument` | `src/Core/NodeLibraries/GASGraph/GraphConfig.cs` |
| Minor | 旁路 API 变成公开死代码：`GraphRegistryScriptResolver.RequireId(string)` 与 `RequireProgram(registry, string graphKey)` 已无任何调用方，但仍公开，等于把"用图名绕过两本清单"的入口留在门口 | `src/Core/Gameplay/AI/BehaviorTree/GraphRegistryScriptResolver.cs` L11、L39–L40 |
| Minor | 零旁路只换了解析终点：`BehaviorTreeScriptKeys` / `HfsmScriptKeys` / `LevelScriptKeys` 仍是 Core 里的硬编码常量表，只是把 `"Graph.BT.Leaf.Patrol"` 换成 `"bt.patrol"`。内容名字仍写死在引擎代码里，不是数据驱动 | `BehaviorTreeOps.cs` L39–L47；`HfsmDefinition.cs` L6–L12；`LevelScriptPrograms.cs` L9 |
| Minor | Showcase 仍是工程术语而非玩家故事：`Metrics.Detail` 改成 "BT Script leaves from ActionLib" / "HFSM Scripts from ActionLib"；`showcase.registry.json` 摘要写 "L1 Script 最小演示：Call/Yield/HaltReturnInt 喝水直到满"。新玩家在画廊里看到的是操作码名 | 五个 `CapabilityStandard*Runtime.cs`；`showcase.registry.json` |
| Minor | `LoadCoreScriptsAndFuncLib` 现在也加载 ActionLib，名字未更新 | `GraphRegistryTestBootstrap.cs` L15–L20 |
| Minor | 加载顺序与合同 §3.7 建议不同：实现是 graphs → func_lib → patch → action_lib，合同写 graphs → func_lib → action_lib → patch。功能等价（patch 只解析 FuncLib 名），但两处没有一处记录这个偏差 | `GameEngine.cs` L906–L918；合同 §3.7 |
| 债 | 删除 `GraphAuthoringFormatPerfCompareTests.cs` 顺带带走了图 VM 的吞吐基准（native / Python / Node 对照），未补替代基准 | 已删文件 |
| 债 | `InvokeScript` 每次调用都全量扫描被调程序找 Yield（`RequireNoYield`），O(n) 落在热路径上。无分配，但同一判定在登记期做一次即可 | `GasGraphOpHandlerTable.cs` L659、L697–L707 |
| 债（预存在，非本 PR 引入，但仍是红的） | `MobaDemoMod` 的 `Graph.Shield.Absorb` 把 `LoadAttribute` 的入参写成 `target`（线性方言只认 `source`），导致引擎初始化直接抛错，5 个生产测试红：`GenerateGasProductionReport`、`MobaDemo_EntryMap_CastQ_DamagesEnemy`、`MobaBootstrap_GameStart_RegistersGameplayInputSystemsInFixedStep`、`MobaDemoLog`、`ProdModSmoke_MobaDemoMod`。基线 #895 同样红，但旧编译器删除后这条资产已彻底无路可走 | `mods/showcases/moba_demo/MobaDemoMod/assets/Configs/GAS/graphs.json` |
| 债（预存在） | `MathOpsChain_Stress_ZeroAllocation` 实测分配 880040 字节（门槛 64），违反 0-alloc 纪律；基线同样红 | `src/Tests/GasTests/Effect/EffectPhaseStressTests.cs` L255 |
| 债（预存在） | GasTests 首次干净构建必失败：多个 showcase mod 的同名内容项（`assets/Presentation/performers.json`、`assets/Maps/*.json`）并行拷贝到同一输出路径，18 个 MSB3021/MSB3027；重跑一次即过。两个分支均可复现 | `src/Tests/GasTests/GasTests.csproj` 的内容项 glob |

### 3.5 合同逐条符合性

**§3.3 FuncLib**

| 合同条目 | 结论 |
|----------|------|
| 资产新增 `purity`，默认 `pure`，非 pure 拒绝 | 达标（默认值 + 严格比较；测试覆盖 `impure` 被拒） |
| 允许 kind：Script(pure) / Score / Validation | **不达标**：加载放行，调用侧只接受 Script（M1） |
| 加载后校验目标图无 Yield/Wait | **部分达标**：`Wait` 编译期降为 `Yield`，单层扫描有效；跨图与热替换两条路径未覆盖（B2） |
| `InvokeFunc` 或 `InvokeScript.functionName` 二选一，禁止两套名字并存 | **部分达标**：线性方言只认 `functionName`（达标）；Script 方言仍保留 `graphId` 直绑（B2a） |
| 所有 L1 Kind 前门白名单包含该调用节点 | 达标：Effect/Score/Validation/Derived 均可编译，测试覆盖 |
| 未登记名失败关闭 | 达标：`PatchFuncLib` → `catalog.Require` 抛错（探针 P5 实测） |
| `readsBlackboard` 显式声明、默认 false | **未实现**：加载器不读该字段；未知字段一律静默忽略（`purity` 拼错会被当默认 pure） |

**§3.4 ActionLib**

| 合同条目 | 结论 |
|----------|------|
| 新资产 `GAS/action_lib.json`，`name`/`graph`/`kind=Script` | 达标，已进 `config_catalog.json` |
| 目标图允许 Yield、允许副作用 | 达标（不做限制），但也不校验"是否真能挂起"（M5） |
| 不得与 FuncLib 同名 | **部分达标**：`GameEngine` 路径有效；API 默认参数 fail-open（M2） |
| Effect/Score/Validation/Query/Derived 前门不得出现 Action 调用 | 达标：线性方言只能按名字调 FuncLib，名字解析只查 FuncLib 清单，ActionLib 名一律失败关闭（探针 P5） |
| L2 解析只走 ActionLib 或 Registry，禁止私藏程序宇宙 | 达标：五个 Showcase + 四个 AI 测试全部改走 `RequireActionId`；`RequireId(string)` 已无调用方（但仍公开，见 Minor） |
| 续跑由宿主持有 cursor + 寄存器 | 达标：`BehaviorTreeWorld` 持 `GraphExecutionCursor[]` 并 `ExecuteSlice`（L270–L299）；但没有一条 `bt.*` 资产真会挂起（M5） |

**§3.5 Effect 阶段表达力**

| 合同条目 | 结论 |
|----------|------|
| A. Effect 线性白名单含 FuncLib 调用 | 达标 |
| B. Effect 允许 `BranchBool`，仍禁 Wait/While/Yield | 达标：`IsBranchBoolAuthorable` 只放开 Script/Effect；`BranchBool` 只降为 `JumpIfFalse` + `Jump`，不引入新 opcode；Wait/Yield/While/Until/SwitchInt 在 Effect 全部失败关闭，5 个 TestCase 覆盖 |
| Score/Validation/Derived 拒绝 BranchBool | 达标，3 个 TestCase 覆盖 |
| 禁止用 Effect 内 While+Wait 模拟 Period | 达标（同上白名单） |

**§5 边界**

| 边界 | 结论 |
|------|------|
| 禁止平行第二套 VM / 作者边模型 | 前进但未收尾：旧编译器已删，遗留 DTO `GraphConfig`/`GraphNodeConfig` 仍在 |
| 禁止 Effect 事务中途 Yield / 调 Action | 达标（编译期 + patch 期 + 运行期三重） |
| 禁止 FuncLib 条目含 Yield 或未声明产生副作用 | **不达标**（B2） |
| 禁止跨库同名；禁止缺文件/缺条目静默空表 | **不达标**（B1、M2） |
| 禁止用 ActionLib 替代 Duration/Period | 达标（无相关改动） |
| 禁止编译期文本 Macro | 达标 |
| L2 不得私藏 Dictionary 程序宇宙 | 达标 |
| 热路径 0-alloc；CallStack 调用方自备 | 基本达标：`HandleInvokeScript` 全 `stackalloc`；解析用的 lambda 闭包只在 `EnsureWorld` 冷路径。`RequireNoYield` 热路径重复扫描记为债 |

**§6 UAT**：六条场景中,"含 Yield 的图不能进 FuncLib"有对应自动化；"Effect 阶段调用 FuncLib"、"Score 可调 FuncLib"只有编译/patch 层断言,缺运行期"阶段完成且不跨拍挂起"的断言；"技能阶段不能调用 ActionLib"零测试且措辞与实现不一致；"用模板字段而不是 Yield 定义 DoT"、"行为树叶子断点续跑"无映射。整体记 M4。

---

## 4. 场景（业务语言）

1. **策划把 `action_lib.json` 误删或清空后启动游戏**
   引擎一声不响照常起来，进关那一刻守卫的第一次思考直接报错崩掉。作者会以为是关卡问题，而不是配置清单没了。

2. **作者写一个"看起来是纯算式"的共用函数，里面偷偷跳进巡逻动作**
   登记时系统承认它是纯的；直到某个技能真的结算到这一步，才在技能事务半途抛异常。事务已经做了一半。

3. **作者在工作台把一条已登记为纯算式的技能图热改成"走一步等一拍"**
   热应用照常成功；下一次任何技能阶段调用这个算式就会当场报错。两本清单谁都没拦。

4. **维护者看 CI 绿灯，以为思考波性能没问题**
   门槛已经放到 40ms，而产品口径是 5ms。真实退化要跌到 8 倍以上才会被发现。当前实测 3.5ms，等于这道闸门形同虚设。

5. **作者想写"给关系加一点好感度"或"按半径查一圈目标"**
   引擎里这些节点还在跑、还有测试，但唯一的作者前门已经不认它们了，写进 JSON 只会得到"未知节点"。文档还告诉他这条链路是通的。

6. **新玩家打开"Script 原子流程沙盘"画廊页**
   看到的介绍是"L1 Script 最小演示：Call/Yield/HaltReturnInt 喝水直到满"。他不知道该点什么、会看到什么。

7. **想验收"可挂起动作"的人去找例子**
   十一条动作里只有"喝水直到满"真的会跨拍；巡逻、追击、攻击、警戒全都是一拍算完的常量图。合同里那条"巡逻走一步再想、下一拍从断点继续"的验收场景，仓库里没有对应内容。

---

## 5. 边界

**本审计覆盖**

- `git diff pr895 pr911` 全量 77 文件，逐文件读过；其中 #911 本体 59 文件逐段核对
- 合同 §3/§5/§6 与实现的逐条对照
- GasTests 全量运行（2286 项）+ 基线对照运行 + 6 条一次性边界探针
- 44 个无作者路径 opcode 的机械枚举与抽样人工复核

**本审计不覆盖 / 不替代**

- Champion 火→冰真机录屏（交接 §5 已列为已知 Xvfb 缺口，非本 PR 主交付）
- #909 cherry-pick 内 LSW 落盘映射完备性（M4 只复核了 `saveModRoot` 注入）
- Raylib / Web 适配器与 UI 面板路径
- 修改任何生产代码：本次交付只有本报告与目录链接；6 条探针测试跑完即删，未进入交付

**合入顺序提示（交接 E.14）**：#911 叠在 #895 上并已 cherry-pick #909，因此 #909 不应再单独合入，否则重复。#910 是合同 docs→main，与 #911 内 `graph-funclib-actionlib-contract.md` 是同一文件的两个版本（#911 版多一行"状态：已落地"）——需产品/维护者决定单一来源，避免两条路径同时改同一页。本审计的立场是：在 B4 处置写清之前，那一行"已落地"不应进 main。

---

## 6. UAT（合入前必须成立）

```gherkin
Feature: 复用清单丢失必须当场拦住
  作为技能与关卡作者
  我希望配置清单缺失或写空时启动就报错
  以便我在进关之前就知道自己改坏了什么

  Scenario: 动作清单文件不存在
    Given 配置目录声明了动作清单
    And 磁盘上没有这份清单文件
    When 我启动游戏
    Then 启动必须失败
    And 失败信息必须点名缺失的清单路径

  Scenario: 动作清单是空表
    Given 动作清单文件内容是一张空表
    When 我启动游戏
    Then 启动必须失败并说明清单不得为空

Feature: 纯算式不得偷偷挂起
  作为技能作者
  我希望被登记为纯算式的图在任何情况下都不会让技能结算跨拍
  以便技能事务不会做到一半停住

  Scenario: 纯算式里跳进可挂起的动作图
    Given 一张图自己不含挂起节点
    And 它直接引用了一张含挂起节点的图
    When 我把它登记进纯算式清单
    Then 登记必须失败
    And 失败原因必须说明该图会间接挂起

  Scenario: 热改把纯算式换成可挂起的图
    Given 技能工作台里有一条已登记为纯算式的技能图
    When 我把它热改成含「等一拍」的图并提交
    Then 提交必须被拒绝
    And 运行时的纯算式清单必须保持原状

Feature: 思考波性能门槛说到做到
  作为维护者
  我希望性能闸门反映产品口径
  以便真实退化能被 CI 拦住

  Scenario: 一万人思考波守住五毫秒
    Given 一万个代理按 60 帧节奏每 12 帧思考一次
    When 连续跑满 25 波
    Then 平均与 p95 单波耗时都必须低于产品口径
    And 超过五毫秒的波次数量必须为零

Feature: 作者面缩编必须写在明面上
  作为图作者
  我希望文档写的能写的节点就是真的能写
  以便我不会照着文档写出无法加载的图

  Scenario: 文档承诺的节点可以真的写出来
    Given 架构文档描述某个节点族可由作者编写
    When 我按文档把它写进图资产
    Then 加载必须成功
    Or 文档必须已明确标注该节点族当前不可编写并给出跟踪单

  Scenario: 查询结果被截断时必须失败关闭
    Given 一张图声明查询必须拿到完整结果
    When 运行时因容量上限丢弃了目标
    Then 执行必须报错中止
    And 自动化测试必须覆盖这条行为

Feature: 可挂起动作要有真实的玩家故事
  作为新玩家
  我希望看到守卫真的「走一步、想一想、下一拍接着走」
  以便相信这套动作库不只是换了个名字

  Scenario: 巡逻叶子跨拍续跑
    Given 巡逻动作登记在动作清单里且含挂起节点
    And 行为树叶子绑定该动作
    When 守卫执行巡逻且当拍没走完
    Then 下一拍必须从断点继续
    And 画廊页的介绍必须用玩家能读懂的话描述这一幕
```

---

## 附录 A — 证据产物

### A.1 边界探针实测（B1、B2a、M1、M2、§3.4）

6 条一次性 NUnit 探针，读现有生产加载器与编译器的实际行为，跑完即删（未进入交付代码）：

```text
P1  missing-file load succeeded. ActionCatalog.Count=0
P1b empty-array  load succeeded. ActionCatalog.Count=0
P2  caller compile succeeded=True diagnostics=0
P2  FuncLib load succeeded. Count=1 pure.looking -> graphId 2
P3  FuncLib Score entry accepted at load. Count=1
P3  InvokeScript kind gate says: Graph program id 1 kind is 'Score', but 'Script' is required.
P4  ActionLib loaded without FuncLib argument. ActionCatalog.Count=1 (FuncLib also holds 'shared.name')
P5  Effect compile succeeded=True
P5  patch stage says: Graph function 'bt.patrol' is not registered.
```

| 探针 | 构造 | 结论 |
|------|------|------|
| P1 | 目录声明了 `GAS/action_lib.json`，磁盘无该文件 | 加载成功、清单为空 → B1 |
| P1b | 同上，文件内容为 `[]` | 加载成功、清单为空 → B1 |
| P2 | Script 图用 `InvokeScript.graphId` 直绑一张含 `Yield` 的图，再以 `purity: pure` 登记 FuncLib | 编译零诊断、登记成功 → B2a |
| P3 | FuncLib 登记 `kind: Score` | 登记成功；调用侧 kind 闸门拒绝 → M1 |
| P4 | 构造 `GraphActionCatalogLoader` 时省略 FuncLib 参数，且两库同名 | 同名条目被接受 → M2 |
| P5 | Effect 图按名字调 ActionLib 条目 `bt.patrol` | 编译通过、符号 patch 阶段失败关闭 → §3.4 达标，但措辞与合同"编译必须失败"不一致（M4） |

### A.2 思考波性能实测（B3）

同机同轮、Debug、`--no-build`，测试 `GraphBehaviorArenaAcceptanceTests.CombinedThinkWaves_60fpsCadence_AiEvery12Frames_StayUnderFiveMs`：

```text
基线 #895 (99bdad19c)，门槛 avg<15 / p95<15：
  waves=25 A=10000 N_topo=8 avg=3.702 p95=3.738 max=3.742 over5ms=0 phase=2  -> PASS

本 PR #911 (20bf1e031)，门槛 avg<20 / p95<40：
  waves=25 A=10000 N_topo=8 avg=3.546 p95=3.595 max=3.668 over5ms=0 phase=2  -> PASS
```

### A.3 无作者路径的 opcode 枚举（B4）

方法：取 `GraphOps.cs` 中 `GraphNodeOp` 的 120 个成员（不含 `None`，全部显式赋值），逐个在 `GraphControlFlowCompiler.cs` / `.Linear.cs` / `.Query.cs` 三个分部类中查找 `GraphNodeOp.<成员>`，未出现即无作者路径。结果 44 个：

```text
ConstBool                       DivFloat                        MinFloat
MaxFloat                        ClampFloat                      AbsFloat
NegFloat                        CompareGtFloat                  HasTag
CompareEqEntity                 SelectTagInMask                 LookupTagDisplayToken
QueryRadius                     QuerySortStable                 QueryLimit
AggMinByDistance                FanOutApplyEffect               ApplyEffectDynamic
FanOutApplyEffectDynamic        FanOutDispatchEffect            FanOutDispatchEffectDynamic
SendEvent                       LoadContextSource               LoadContextTargetContext
RelationshipEnsureLink          RelationshipRemoveLink          RelationshipSetMetric
RelationshipAddMetric           RelationshipGetMetric           RelationshipHasFlag
RelationshipSetFlag             RelationshipQueryBetweenPair    RelationshipHasLink
LoadTargetPosX                  LoadTargetPosY                  ClampTargetToRange
IsPointInCircle                 SnapToNearestInCollection       SnapToNearestGraphEdge
LoadEventPayloadInt             LoadEventPayloadFloat           ControlDomainResolve
ControlDomainControls           KnowledgeHasProjection
```

抽样复核：`RelationshipEnsureLink`、`RelationshipSetMetric`、`ApplyEffectDynamic`、`SendEvent`、`SelectTagInMask`、`QueryRadius`、`ControlDomainResolve` 仍各有 3–10 处 VM 层测试引用（手工构造 `GraphInstruction[]`），即运行时有覆盖、作者面无入口；`SnapToNearestInCollection` 现在全仓零测试引用。

### A.4 复现要点

```text
# 全量（2286 项，6 红：5 条 Graph.Shield.Absorb 连坐 + 1 条 0-alloc，基线同红）
dotnet build src/Tests/GasTests/GasTests.csproj -c Debug -m:1     # 首次干净构建会因同名内容项并行拷贝失败，重跑一次
dotnet test  src/Tests/GasTests/GasTests.csproj -c Debug --no-build

# 本 PR 直接相关子集（86 项全绿）
dotnet test src/Tests/GasTests/GasTests.csproj -c Debug --no-build \
  --filter "FullyQualifiedName~Ludots.Tests.Gas.Graph|FullyQualifiedName~Ludots.Tests.Gas.AI"

# 基线对照
git worktree add /tmp/base895 <pr895-head>
```

---

## 附录 B — 给修复 Agent 的最短提示词

```text
修复 PR #911（FuncLib/ActionLib）审计阻断项，报告见
docs/audits/pr911_funclib_actionlib_architecture_audit.md，合同见
gitbook/architecture/graph-funclib-actionlib-contract.md。
只改自己工作区；NO FALLBACK、SSOT、Data-Driven；先读
gitbook/contributing/ai-assisted-development.md 的任务执行决策规范。

按顺序做，每项都要有测试：

B1 两个清单加载器：目录已声明但文件不存在、或合并结果为空表 → 抛错关闭。
   GraphActionCatalogLoader.cs / GraphFunctionCatalogLoader.cs。
   补两条测试：缺文件、空数组。

B2a FuncLib 登记时做可达性检查：沿 InvokeScript（含 graphId 直绑）与 Call
   递归判定被调图是否含 Yield，含则拒绝登记并点名路径。
   同时决定 Script 方言的 InvokeScript.graphId 去留——保留就要说明它凭什么
   不算旁路，删除就同步改文档。禁止只靠运行时 RequireNoYield 兜底。

B2b 热应用路径：ReplaceProgram 之前（或 LiveGasEditPipeline 分类阶段）复检
   该图是否被 FuncLib 登记；已登记则新程序不得含 Yield，违反即拒绝提交。

B3 GraphBehaviorArenaAcceptanceTests：门槛还原到不高于基线（实测 avg≈3.55、
   p95≈3.60），把 over5ms 计数改成断言；方法名与断言必须一致。
   不准以「CI 噪声」为由放宽，除非附同机多轮实测数据。

B4 二选一并写清：
   (a) 补回作者路径与守卫测试；或
   (b) 把作者面缩编写进 graph-funclib-actionlib-contract.md 与
       graph-layering-flow-and-behavior.md，逐族列出当前不可编写的节点，
       为 AllowTruncated/droppedOutput、validOutput、以及 44 个无作者路径的
       opcode 开跟踪单，并把 GAS.GRAPH.ERR.SpatialQueryIncomplete 的失败关闭
       行为补一条 VM 层测试。
   无论哪条，contract 第 3 行的「状态：已落地」都要改成与事实一致的措辞。

M1 FuncLib 若继续允许 Score/Validation，就要提供能调用它们的作者节点；
   否则加载期直接拒绝非 Script kind。不许登记成功、调用炸。
M2 GraphActionCatalogLoader 的 FuncLib 参数改为必填。
M3 GraphRegistryTestBootstrap 改为调用生产加载器
   （GraphFunctionCatalogLoader / GraphActionCatalogLoader），删掉手写的第二套
   加载逻辑；顺带把方法名改成反映它也加载 ActionLib。
M4 为合同 §6 建 gitbook/acceptance/graph-funclib-actionlib-uat.md，逐条映射
   自动化测试名或明确标注为债；「技能阶段不能调用 ActionLib」补测试，并把合同
   措辞从「编译必须失败」改成与实现一致的「作者前门链路必须失败关闭」。
M5 决定 ActionLib 的准入是否要有内容层判定；至少给一条 bt.* 叶子写出真会跨拍
   的动作，让「可挂起」在 L2 上有一条活的证据。

预存在但仍红、建议单独开票，不要夹带进本次修复：
- mods/showcases/moba_demo 的 Graph.Shield.Absorb 把 LoadAttribute 入参写成
  target（线性方言只认 source），导致引擎初始化抛错、5 个生产测试红。
- MathOpsChain_Stress_ZeroAllocation 实测分配 880040 字节。
- GasTests 首次干净构建因同名内容项并行拷贝失败。
```
