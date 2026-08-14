# main 图能力收口架构审计（#932 落地后 · SSOT）

**被审对象：** `origin/main` @ `82ddb3322a`（落地 PR [#932](https://github.com/MightyBubble/Ludots/pull/932)，已合）
**审计需求：** PR [#939](https://github.com/MightyBubble/Ludots/pull/939) → `docs/audits/pr932_graph_landed_audit_handoff.md`
**前序审计：** `docs/audits/pr911_funclib_actionlib_architecture_audit.md`（#914）、`docs/audits/pr911_audit_fix_checklist.md`
**合同：** `gitbook/architecture/graph-funclib-actionlib-contract.md`（状态：修复中 / Epic #915）

本文件是本轮审计的唯一结论。九个领域（A–H2）的分头取证已在此合成，不另留平行报告。

---

## 1. 概述

### Verdict：FIX-FORWARD（带闸门）

**不是 HOLD MAIN，也不是 REGRESS。** 玩家门这件事本身是真做成了：120 个还能运行的图节点，每个有一间自己的展厅、一份自己的分镜、一张自己的地图、一间自己的薄入口，人是地图刷的，字幕模板是分镜给的，全量 GAS 测试 2453 条全绿。这不是「仓库变大了」，是能玩的东西真的多了 120 间。

但有四条必须在「合同改回已落地」和「再合任何新图能力」之前关掉，其中三条是红线：作者今天就能从纯查询管道里调起可挂起动作；已经宣布退役的八间大杂烩玩家照样点得进去，而其中五间进门就会清空引擎的图编号表；玩家看到的血条，数字是真图算的，但落到世界上那一步是 C# 直接摆的，还带一条「掉到 0 就偷偷回满」的暗规则。

### 玩家一句话

打开启动器，你看到的是一长串能点的展示，不是被塞进某一关；点「两段伤害叠在一起」，场上真有施法者和木桩，字幕真是策划写在分镜里的那句话。但那根血条只在少数几间里代表「真被打掉的血」，多数时候它是「有事发生了」的通用指示灯；而目录里划掉的八间旧展厅，门牌摘了，门没锁。

---

## 2. 结构（阶段 / 领域对照）

| 阶段 | 领域 | 一句话结论 |
|------|------|------------|
| 1 | A 启动器 / 登记表 | 无硬编码默认关；120 条字段齐全；退役家族 preset 已删但 binding 还在 |
| 1 | B 分镜 / 地图 / 字幕 | 人与字幕模板真数据驱动；填字幕的中文词与「演什么」仍在 C# 里按 op 名分支 |
| 2 | C 开图 / 空间 / 效果队列 | 画廊走正式开图且等到空间索引就绪；效果队列「清空」不真清；家族场走平行世界 |
| 2 | D 血条 / 披露 / 描边 | 披露管道是真的（Knowledge + WorldHud）；血量数值大量由 C# 摆放 |
| 3 | E FuncLib / ActionLib | 加载期失败关闭全部到位；Query 前门是唯一未闭合的开口 |
| 3 | F 作者前门 | 44 个曾无前门的 opcode 全部补齐，缺口 0；`AllowTruncated` / `validOutput` 写不出来 |
| 3 | G 覆盖表 | 枚举/展厅/资产三个维度不假绿；`unitTestFilter` 有 21 条错误归因，守卫结构上抓不到 |
| 4 | H1 退役家族场 | 门牌摘了门没锁；五间进门清空 `GraphIdRegistry`；两处「未完工先写成功文案」 |
| 4 | H2 L2 叶子 / 技能热改 | 跨拍续跑与未知 Tag 失败关闭都是真的；热改在会话层非原子 |
| 5 | 合成 | 本文件 |

---

## 3. 详情

### 3.1 阻断（3 条）

| # | 问题 | 证据 | 为什么是阻断 |
|---|------|------|--------------|
| **B1** | **Query 前门允许 `InvokeScript.graphId`，作者可从 L1 纯管道直接调起 ActionLib 动作。** 线性方言（Effect/Score/Validation/Derived）明确拒绝 `graphId`；Query 方言只拒绝「两个都给」和「两个都不给」，只给 `graphId` 一路放行并原样编译成 `Imm = node.GraphId; Flags = 0`。运行期 `RequireKind(Script)` 对 ActionLib 条目天然通过，`RequireNoYield` 只拦得住含 Yield 的那 2 张图，`action_lib.json` 里另外 **9 个条目不含 Yield**，全部可被一张 Query 图调起 | `src/Core/NodeLibraries/GASGraph/GraphControlFlowCompiler.Query.cs`（`ValidateQueryNode` 的 `InvokeScript` 分支；`CompileQueryNode` 的 `else` 分支）对照 `GraphControlFlowCompiler.Linear.cs`（`cannot use graphId in linear FuncLib authoring`）；`assets/Configs/GAS/action_lib.json` | 直接违反合同 §3.4「**Effect / Score / Validation / Query / Derived 前门不得出现** Action 调用」。这是作者今天就能写出来的，且零测试覆盖——`FrontDoor_LinearKindsInvokeScriptGraphId_FailClosed` 的参数列表里没有 `Query`，也没有 `Effect` |
| **B2** | **退役的八间家族展厅玩家仍点得进去，而其中五间进门就清空引擎图编号表。** 登记表这边做干净了（`status=retired`、`preset=null`），但 `binding` 八条全留着，`launcher.config.json` 里八条 binding 全是活的；启动器 `launchHint()` **先看 `binding`、完全不看 `status`**，于是每张退役卡照样吐出可复制的 `ludots launch $capability_standard_graph_ops_attr --adapter raylib`。而 Rel / Query 两间在 `GameStart` 上对活引擎直呼 `BindStandaloneFromModAssets()`，落到 bootstrap 的 `GraphIdRegistry.Clear()`；Attr / Spatial / Event 三间的 bootstrap 里也有同一句 | `src/Tools/Ludots.Launcher.React/src/lib/showcase.ts`（`launchHint` 先 `if (entry.binding)`）；`src/Tools/Ludots.Launcher.React/src/components/ShowcasePanel.tsx`（retired 只加灰徽章，可见性只按 tier/搜索词过滤）；`launcher.config.json`；`CapabilityStandardGraphOpsRelModEntry.cs` / `CapabilityStandardGraphOpsQueryModEntry.cs` 的 `GameEvents.GameStart` → `BindStandaloneFromModAssets()`；`GraphOpsRelShowcaseBootstrap.cs` / `GraphOpsQueryCatalogBootstrap.cs` / `GraphOpsAttrGraphBootstrap.cs` / `GraphOpsSpatialCatalogBootstrap.cs` / `GraphOpsEventGraphBootstrap.cs` 的 `GraphIdRegistry.Clear()`；`src/Core/NodeLibraries/GASGraph/Host/GraphIdRegistry.cs`（`Clear()` 连 `_frozen` 一起复位、`_nextId` 归 1） | 「八个家族大杂烩退役，不是第二套玩家入口」是已裁决共识。现在它既没退役到玩家碰不到，进去还会把引擎在 init 期注册好的全部图编号清掉、把 1..N 重新分给家族自己的图，而引擎的 `GraphProgramRegistry` 仍按旧编号存程序——同进程两套编号打架。**「玩家点得进」与「进去就破坏核心注册表」这两条单独看都是 Major，叠在一起才是阻断** |
| **B3** | **玩家看到的血条，数字来自真图，落地那一步是 C# 摆的，还带一条暗规则。** 每间展厅先跑真 VM（`GasGraphOpHandlerTable.Execute`，全 `stackalloc`、真 `BuiltinHandlerRegistry` / `EffectTemplateRegistry` / `EffectRequestQueue` 根），取出寄存器里的数——这一半是真的。但把这个数变成血条的那一步是 `next -= 图返回值` 之后 `AttributeMutationOps.SetBase/SetCurrent` **直接写属性**，不走任何效果结算；且 `if (next <= 0f) next = opening;`——血要掉到 0 就静默回卷成开局值。C# 影子数组 `ctx.ActorHealth` 每 tick 被 `SyncHud` 无条件刷回世界 | `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/Runtime/Drivers/LinearNodeDriver.cs`（`next <= 0f` 回卷 + `WriteHealth`）；同目录 `BlackboardNodeDriver.cs` / `EventNodeDriver.cs` / `ScriptNodeDriver.cs`（`ctx.ActorHealth[i] = …` 直接赋值，Script 那间写的是「茶水量」）；`Runtime/GraphOpsNodeActorBinding.cs`（`SyncHud` → `WriteHealth`、`WriteHealth` → `AttributeMutationOps`）；`Runtime/IGraphOpsNodeDriver.cs`（`ActorHealth` 影子数组） | 违反已裁决共识第 6 条「血条走 WorldHud / 生命披露；**禁止 C# 改血演戏驱动 HUD**」。而且登记表当着玩家的面写「血条按总和往下掉」（`showcase.registry.json` 的 `AddFloat` summary），玩家没法区分哪根条是真结算、哪根是指示灯。**回卷那一句是 NO FALLBACK 红线**：本该「打死了」的时刻被静默改写成「满血重来」，且这条规则不在任何分镜数据里 |

**B3 的另一半必须同时讲清，否则会误伤**：披露这一侧是真的。`graphops.hud.health_bar` 从 `entity_health_bar` 继承 `AttributeBinding(Health)`，`WorldHudPerformBehavior` 按 `KnowledgeProjectionStore` 的属性掩码决定发不发这根条，`PerformPhaseResolver` 的 `allowWorldHudProjection` 是真门。六角圈人那三间的语义完全干净：圈外的人还站在场上、世界血量仍是作者数据 100、只是 HUD 不披露，且有测试逐档断言（`GraphOpsNodeGallerySpatialAcceptanceTests.QueryHexRing_LightsOnlyTheRing_NotInsideOrOutside`）。**问题在数值来源，不在披露管道。**

### 3.2 Major

| # | 问题 | 证据 |
|---|------|------|
| M1 | `EffectRequestQueue.Clear()` 不是清空：`_count = 0` 之后立刻 `RefillFromOverflow()` 把 overflow 环里的旧请求灌回主缓冲，`_budgetFused` / `_nextRootId` / `_dropped` 全不复位。换展厅时 `ClearQueuedEffects` 调的正是它，FanOut 家族把队列打到 overflow 之后换展厅，下一拍会派发指向已销毁实体的旧请求 | `src/Core/Gameplay/GAS/EffectRequestQueue.cs`（`Clear` / `RefillFromOverflow`）；调用点 `mods/showcases/capability_standard/CapabilityStandardGraphBehaviorCommon/GraphOpsHeadlessGameEngine.cs`（`ClearQueuedEffects`） |
| M2 | `InvokeBuiltin` 那间展厅每 0.35 秒 materialize 一个新身体，整个画廊 mod 里**没有任何 `Destroy` / `Despawn`**。玩家站着不动实体数就无限涨，且这些人不在分镜名单里、没名字没血条 | `…NodeGalleryMod/assets/GAS/graphs/InvokeBuiltin.json`（`InvokeBuiltin` → `MaterializeTemplate`）；`…/Runtime/GraphOpsNodeGalleryRuntime.cs`（`Tick` 每 `ThinkPeriodSeconds=0.35` 重跑）；`rg "Destroy\|Despawn"` 在该 mod 下零命中 |
| M3 | 覆盖表 `unitTestFilter` 有 **21/120** 条错误归因：登记的画廊测试根本不执行该 op。最典型是 event 家族 15 个 op 全指向 `SnapToNearestInCollection_SucceedsWithPlayerCaption`（一个单 op 测试），而 `SendEvent_BroadcastsPlayerReadableHit`、`ClampTargetToRange_PullsLandingPointInRange`、`LoadViewer_ReadsTheAudience`、`KnowledgeHasProjection_ShowsVisible`、`SnapToNearestGraphEdge_SnapsOntoTheRoad` 就在同一个类里却零引用；`ConstFloat` / `AddFloat` 指向 `FloatFamilyOp_RendersPlayerCaption`，而那个 `TestCaseSource` 数组里没有这两个 op | `assets/Configs/GAS/graph_node_op_coverage.registry.json`；`src/Tests/GasTests/Production/GraphOpsNodeGalleryEventAcceptanceTests.cs`、`GraphOpsNodeGalleryFloatAcceptanceTests.cs`；`scripts/generate-graph-op-node-galleries.py`（`DRIVER_FAMILY_TEST` 按 `driver` 字段盲配，不校验 op 是否在被配测试的列表里） |
| M4 | 覆盖表守卫在结构上不可能发现 M3：`LoadGasTestMethodNames` 只按**方法名**建集合、丢掉类名，校验时只问「全 GasTests 里有没有这个方法名」；`hasGalleryTest` 只查前缀是否 `GraphOpsNodeGallery`，而这两条 token 是生成器无条件注入的——生成器写什么守卫就查什么，闭环自证。另：`status` 只剩 `covered` 一个合法取值（生成器拒绝非 covered，守卫也把非 covered 记为 failure），P2 计划里的 `missing` / `runtime-only` 已无法表达，「120/120 covered」是被结构保证的，不是被度量出来的 | `src/Tests/GasTests/Graph/GraphNodeOpCoverageRegistryTests.cs`（`LoadGasTestMethodNames`、`CoveredEntries_RequireRegisteredGalleryAndGalleryTestFilters`）；`scripts/generate-graph-op-node-galleries.py`（`coverage_filters`、`GALLERY_ID_TEST` / `GALLERY_TICK_TEST`） |
| M5 | 字幕的**模板**在分镜、字幕的**词汇**在 C#：22 间展厅玩家看到的那句话里至少有一个中文词是 driver 写死的（`在圈里` / `连着` / `全力` / `茶水` / `说了算` / `管得着` / `看得见` / `成立`…），且 120 个 op 里有 81 个的名字被 driver 用 `case "{Op}"` 字符串分支硬编码——「这一格演什么」本身不是数据驱动 | `…NodeGalleryMod/Runtime/Drivers/EventNodeDriver.cs`（`ApplyBeat` 的 15 个 case 各自写死 `CaptionValues["result"]` 的中文）、`BlackboardNodeDriver.cs`、`SandboxNodeDriver.cs`、`RelNodeDriver.cs`、`AttrNodeDriver.cs`、`SpatialNodeDriver.cs`、`ScriptNodeDriver.cs`、`LinearNodeDriver.cs` |
| M6 | `AllowTruncated` 在所有方言里**彻底写不出来**：`RequireSpatialCapacityPolicy` 在策略合法且 `droppedOutput` 已填的情况下**仍然无条件报错** "not yet authorable on ControlFlow linear kinds"，两个方言的 compile 对全部空间 op 硬写 `Flags = 0`。而 `graph-layering-flow-and-behavior.md` 写的是「后者必须同时声明 `droppedOutput`（未声明则失败关闭）」，读者会理解成声明了就能用。修复清单 P1「前门字段 + 失败关闭守卫已恢复」这句名不副实：守卫恢复了，前门字段没恢复成可用 | `src/Core/NodeLibraries/GASGraph/GraphControlFlowCompiler.Linear.cs`（`RequireSpatialCapacityPolicy`）；`gitbook/architecture/graph-layering-flow-and-behavior.md`；全仓 `*.json` 搜 `AllowTruncated` 零命中 |
| M7 | `validOutput` 被前门接受但写进**共享 scratch 寄存器 B[31]**（`GraphVmLimits.MaxBoolRegisters - 1`），`TargetListGet` 无条件也写 B[31]，两者相撞；而 `SnapToNearestInCollection` 的输出端口只放行 `value`，那个 bool 没有任何值边能读到。守卫测试名 `SnapWithValidOutput_AllocatesDedicatedBoolRegister` 里的 "Allocates" 和 "Dedicated" 都不成立 | `GraphControlFlowCompiler.Linear.cs`；`src/Tests/GasTests/Graph/GraphControlFlowConfigCoverageTests.cs` |
| M8 | `tag-display-lookup.md` 的门面场景写不出来：§3.7 推荐的 `{ "type": "TextToken" }` 不在 `GraphOutputValueKind` 枚举里；它给的「临时过渡」写法依赖的 `semantic` 字段不在 `GraphOutputConfig` 上，而前门用 `UnmappedMemberHandling.Disallow`，整张图会在反序列化阶段被顶回。§4.1 / §4.3 的示例都按这个形状写 | `gitbook/architecture/tag-display-lookup.md` §3.7/§4.1/§4.3 对照 `src/Core/NodeLibraries/GASGraph/GraphOutputTypes.cs`、`GraphConfig.cs` |
| M9 | 热改在**会话层非原子**：`Classify` 对混合补丁会同时置 `canNextCast=true` 与 `engineRestart=true`（两个独立 `ref bool`），而 `LiveSkillWorkbenchRuntime.TryApplyNextCast` 只判 `!CanCommitNextCast` 就放行，**不查 `RequiresEngineRestart` / `RequiresMapReload`**——于是只提交 NextCast 子集、静默丢弃其余算子，并把状态置成「已应用」。commit 层的回滚（`CommitNextCastSafeFrame`）是真的且有测试，问题在它上面那一层。混合会话零测试覆盖 | `src/Core/…/LiveGasEditPipeline.cs`（`Classify`、`CommitNextCastSafeFrame`）；`LiveSkillWorkbenchRuntime.TryApplyNextCast`；`LiveSkillWorkbenchDataPlane.HandleApply` |
| M10 | 三个 L2 宿主（BT / HFSM / Level）构造 `GraphExecutionState` 时都不填 `Programs`，而 `HandleInvokeScript` 首行即 `if (s.Programs == null) throw`——**ActionLib 动作调 FuncLib 纯函数必抛**。合同 §3.3 把 FuncLib 的调用方明确列为「…Script、ActionLib」。失败是响亮的（不是静默），但这条边界断了 | `BehaviorTreeWorld.EvalLeaf`、`GraphProgramHfsmHost.ExecuteHalt`、`GraphProgramLevelHost.RunScript`；`src/Core/NodeLibraries/GASGraph/GasGraphOpHandlerTable.cs`（`HandleInvokeScript`） |
| M11 | 合同 §4.4 写「HFSM OnTick → ActionLib『警戒一步』（**内含 Yield**）」，实现相反：`GraphProgramHfsmHost.RunAction` 非 Halted 即抛「Yield is not allowed on lifecycle bindings」，Level `RunScript` 同样。且 `GraphActionCatalogLoader` 不按宿主区分 Yield 策略，含 Yield 的 `hfsm.*` 条目**能加载通过、只在运行时炸**——加载期没有失败关闭 | `GraphProgramHfsmHost.RunAction`、`GraphProgramLevelHost.RunScript`；`GraphActionCatalogLoader`；`gitbook/architecture/graph-funclib-actionlib-contract.md` §4.4 |
| M12 | `gitbook/acceptance/graph-funclib-actionlib-uat.md` 是**两份文档拼在一个文件里**（两个 H1），对同一批合同 §6 场景给出互相矛盾的结论：上半说「技能阶段不能调用 ActionLib」已被覆盖，下半同一场景标 `无 / Gap`；上半说 `bt.patrol` resume 已覆盖，下半标「缺一条 `bt.patrolStep` 真 Yield」。上半引用的「Effect 前门拒 `InvokeAction`」还是个**不存在的节点**；`gitbook/SUMMARY.md` 指向的是下半那个旧标题 | `gitbook/acceptance/graph-funclib-actionlib-uat.md`；`gitbook/SUMMARY.md` |
| M13 | **docs-governance 在 main 上仍然是红的**，具体断点：`gitbook/acceptance/graph-funclib-actionlib-uat.md` 第 7 行链到 `architecture/graph-funclib-actionlib-contract.md`，相对解析成 `gitbook/acceptance/architecture/graph-funclib-actionlib-contract.md`（不存在），正确写法是 `../architecture/…`。触发规则 `missing-link-target` | `scripts/validate-docs.ps1`（`missing-link-target` 规则）；本地复跑见 §6 证据 |
| M14 | 合同 §3.3 要求的两条完全未实现：FuncLib 的 **I/O 寄存器约定 SSOT**（整型/浮点/布尔/实体入参槽与返回槽）没有任何文档，实际是 `HandleInvokeScript` 里的隐式约定（子帧只继承 `e[0]=Caster` / `e[1]=ExplicitTarget`，返回只有一个 int 槽）；`readsBlackboard` 字段不存在、Loader 不读。「纯函数」的输入面没有契约 | `GraphFunctionCatalogLoader`；`GasGraphOpHandlerTable.HandleInvokeScript`；`assets/Configs/GAS/func_lib.json` |
| M15 | 八家族的「人」不来自地图：家族地图全是 `"Entities": []` 的空舞台，演员一律 `World.Create()` 现造再传给 `BindMapEntity`（方法名与实情不符）。`GraphOpsEngineWorld.AttachOrCreate` 本身就是 fallback 造型（引擎在就用 `engine.World`，为 null 就 `World.Create()` 造裸世界），八条家族验收测试**全部只跑 null 分支**，引擎分支零覆盖 | `GraphOpsHeadlessGameEngine.cs`（`GraphOpsEngineWorld.AttachOrCreate`）；`CapabilityStandardGraphOpsFloatMod/assets/Maps/*.json`（`"Entities": []`）；八个 `GraphOps*ShowcaseAcceptanceTests` |
| M16 | 两处「未完工先把成功文案写好」。Blackboard 家族里 `LifecycleShowcaseGraphApi` 是写在生产 mod 代码里的假 `IGraphRuntimeApi`：`BeginLifecycleTransaction()` / `InvokeBuiltin()` 只 `++` 计数，其余成员一律返回 `false`/`0`，图里点名的 `TransferStableId` / `ClearActiveEffects` 一次都没跑，玩家文案照播「生命周期事务开启 N 次」，测试也只断言计数 `> 0`；黑板回读五处 `TryGet(...) ? x : 0` 静默降级。Attr 家族自建私有 `EffectRequestQueue` 没人 drain，「上效果」只报队列长度，「卸效果」之前先手工 `Create` 一个 `GameplayEffect` 塞进目标身上 | `CapabilityStandardGraphOpsBlackboardMod/Runtime/LifecycleShowcaseGraphApi.cs`、`GraphOpsBlackboardRuntime.cs`；`CapabilityStandardGraphOpsAttrMod/Runtime/GraphOpsAttrRuntime.cs` |
| M17 | 静态注册表跨引擎污染窗口：同进程第二次 `new GameEngine()` 会清空 `EffectTemplateIdRegistry` / `AbilityIdRegistry` / `GraphIdRegistry`，而共享展厅引擎（`_galleryEngine` 从不 Dispose，寿命等于进程）的实例级 `EffectTemplateRegistry` 仍持旧 id；`ResolveConfigEffect` 是先用静态表拿 id 再去实例表 `TryGet`，错位时**不抛异常而是静默绑到另一个 effect** | `src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs`、`AbilityExecLoader.cs`、`GraphProgramConfigLoader.cs`；`…/Runtime/GraphOpsNodeGalleryHost.cs`（`ResolveConfigEffect`） |
| M18 | 生成器只 upsert 不删除：退役或删掉一个节点会留下全套孤儿门（binding / preset / registry 条目 / 薄入口 Mod / 画廊地图），而 `validate-registry.py` 仍判通过（孤儿两边都在、双向一致）。这正是八条家族 binding 残留的机制。且**没有任何 CI 闸门跑生成器比对漂移**——今天 120/120 对齐是人工正确，不是机制保证 | `scripts/generate-graph-op-node-galleries.py`（`upsert_by_key` 只替换或 append；退役逻辑只改 `status` / `preset`，不动 `bindings`）；`.github/workflows/` 全目录无该脚本引用 |
| M19 | `capability_standard_ability_graph_sandbox`（`status=active`，binding + raylib preset 齐全）是 `GraphOpsStageVisuals.Spawn` 全仓**唯一**的调用方，造的正是「第二套只用来看的人」：玩家看到的方块血量在生成时写死 100/100 之后再没人碰，真在跑图、被挂状态的是另一批没有 `VisualTransform` 的实体 | `CapabilityStandardAbilityGraphSandboxMod/Runtime/AbilityGraphSandboxRuntime.cs`（`SpawnStageVisuals` / `SpawnCombatant`）；`showcase.registry.json` |
| M20 | 好感被直接写成血量：Rel 家族 `SyncFriendVisuals` 把 `Loyalty` 指标当血量画（`_stage.SetHealth(_friendProxies[i], loyalty, 100f)`），掉链的人血条直接写 0——是「死给你看」而不是「不披露」。该 showcase 已 `retired`，但 B2 说明退役不等于玩家碰不到；且 `GraphOpsRelShowcaseAcceptanceTests` 里没有任何 Health 断言 | `CapabilityStandardGraphOpsRelMod/Runtime/GraphOpsRelRuntime.cs`（`SyncFriendVisuals`） |
| M21 | `graphops.hud.health_bar` / `graphops.visual.*` 的 performer 定义在 **11 个 mod 里各存一份逐字节副本**，没有 SSOT；「血条撑满全屏」的修复（`inheritScale: false`）只落在画廊那一份，其余 10 份没修，而那些 mod 并不依赖画廊 mod | 11 份 `mods/showcases/capability_standard/*/assets/Presentation/performers.json` |
| M22 | 五个家族 bootstrap 都会清进程级静态 `GraphIdRegistry`，但只有 Rel / Query 两个 fixture 标了 `[NonParallelizable]`，Attr / Event / Spatial / Script / Float / Blackboard 都没标——CI 串扰风险 | `GraphOpsRelShowcaseAcceptanceTests.cs`、`GraphOpsQueryShowcaseAcceptanceTests.cs`（有 `[NonParallelizable]`）对照其余六个 |
| M23 | `DestroyPendingPresentationActors` 直接 `World.Destroy`，绕过引擎自己的 publish→finalize 两拍销毁协议，且全世界无差别扫；`ResetSpatialIndex` 又排在 `LoadMap` **之后**，第 3 步硬销毁到第 5 步清分区之间整个 `LoadMap`（MapLoaded 触发器、`ParticipantBindingResolver`、`LoadPathingForSession`）都跑在脏分区上 | `GraphOpsHeadlessGameEngine.cs`（`LoadExclusiveMap` 的步骤顺序）；`src/Core/Presentation/Systems/PresentationEntityLifecycleSystem.cs` / `PresentationEntityFinalizeDestroySystem.cs` |
| M24 | Showcase 自证循环：`BehaviorTreeArenaRuntime.ThinkWave` 只用 `Statuses[i] == Running` 计数就写出文案 "BT Script patrol leaf yielding across think waves"，而 `BehaviorTreeArena_PatrolLeaf_YieldsAcrossThinkWaves` 只匹配这句自己造的字符串，并未验证 yield 发生在 patrol 叶子。M5 的内核半边是实的，Showcase 半边不是 | `BehaviorTreeArenaRuntime.ThinkWave`；`src/Tests/GasTests/Production/GraphBehaviorSeparatedShowcaseAcceptanceTests.cs` |
| M25 | `showcase.registry.json` 里 `capability_standard_live_skill_workbench` 的 summary 写「冠军技能真施放…再放成冰球加伤并挂冰冻；头顶血条数字与冰冻倒计时可读」，`acceptanceTest` 指向 `LiveSkillWorkbenchShowcaseAcceptanceTests`，而该 fixture 跑的是 #625 工作台回路，**完全不触及**冰击/冰冻/血条倒计时。火→冰的真实覆盖只有 `LswFireToIceHotApplyTests.HotApply_FireboltToIcebolt_SwapsImpactPresentationAndDamage`，它构造合成注册表、不加载引擎、不施放、不断言目标掉血或挂 `State.LSW.Chilled` | `showcase.registry.json`；`src/Tests/GasTests/Production/LiveSkillWorkbenchShowcaseAcceptanceTests.cs`；`src/Tests/GasTests/LiveSkillWorkbench/LswFireToIceHotApplyTests.cs` |

### 3.3 Minor

| # | 问题 | 证据 |
|---|------|------|
| m1 | `RequireId(string)` 从 #914 记的「无调用方死 API」变成**有调用方的活旁路**：Showcase 生产路径 + 验收测试各一个，按图名绕过两本清单取 id，与 `graph-layering-flow-and-behavior.md`「勿使用已标 obsolete 的字符串旁路」直接冲突。它现在标了 `[Obsolete]` 且失败关闭，但没删 | `CapabilityStandardGraphOpsScriptMod/Runtime/GraphOpsScriptRuntime.cs`；`src/Tests/GasTests/Production/GraphOpsScriptShowcaseAcceptanceTests.cs`；`GraphRegistryScriptResolver.cs` |
| m2 | `RequireProgram(registry, string graphKey)` 是同类字符串旁路但**未标 obsolete**，仍公开 | `GraphRegistryScriptResolver.cs` |
| m3 | 同名守卫只到 **name** 层，不比对 graphId：同一张图可用两个不同名字同时进两本清单，两边都加载成功。`GraphFunctionCatalog.TryGetByGraphId` 现成可用但未用于交叉校验。合同 §5 只写「禁止同名」，所以严格讲这是合同文本的口子 | `GraphActionCatalogLoader.Load` |
| m4 | LSW 纯度校验只在「被编辑图本身是 FuncLib 条目」时触发，缺反向依赖扫描：编辑一张被 FuncLib 经 `graphId` 间接调用的非 FuncLib 图、改成含 Yield，classify 期不拒，失败点后移到运行期（`RequireNoYield` 会抛，不是静默） | `LiveGasEditPipeline.cs`（`_functions.TryGetByGraphId` 触发条件） |
| m5 | `purity` 等字段拼错会被静默吞掉走默认 `pure`（未知字段一律忽略） | `GraphFunctionCatalogLoader.ReadOptionalString` |
| m6 | 线性 `graphId` 拒绝测试只参数化了 Score / Validation / Derived，**漏了 Effect 与 Query** 两个 kind。Query 漏掉不只是覆盖问题，补上就会红（见 B1） | `src/Tests/GasTests/Graph/GraphEffectAuthoringExpressivenessTests.cs` |
| m7 | 三张硬编码 ScriptKeys 常量表仍在 Core，未数据化 | `BehaviorTreeOps.cs`、`HfsmDefinition.cs`、`LevelScriptPrograms.cs` |
| m8 | FALLBACK：featured 节点没有目的寄存器时（`Dst == byte.MaxValue`，控制流类 op 的合法编码）静默改读 0 号寄存器，字幕拿到的是别人的值而不是报错 | `…/Runtime/IGraphOpsNodeDriver.cs` |
| m9 | FALLBACK：找不到人时 driver 用中文兜底名而不是抛错（`"木桩"` / `"无"` / `"未知"` / `"没有人"` / `"无人"`） | `Drivers/BlackboardNodeDriver.cs`、`RelNodeDriver.cs`、`SpatialNodeDriver.cs`、`QueryNodeDriver.cs` |
| m10 | 启动器静默吞异常：`launcher.config.json` 损坏或缺失时 `catch { return new T(); }` 降级成空 bindings，玩家看到「没有绑定」而不是失败关闭；`ludots launch` 不给 selector 时静默回退到字典序第一个预设 | `LauncherConfigService.cs`（`ReadJsonFile`）；`Ludots.Launcher.Cli/Program.cs`、`LauncherService.cs`（`ResolveSelectedPresetId`） |
| m11 | 12 个 op 的 `vignette.graphKind` 与图 JSON 的 `kind` 不一致（分镜写 `Query`，图写 `Effect`）：编译按图 JSON 的 kind 走，策略校验按分镜的 kind 走，而 `ctx.Kind` 全仓无 driver 读取 | `…/Runtime/GraphOpsNodeGraphCompiler.cs`；`assets/Vignettes/{AggCount,AggMinByDistance,QueryCone,QueryFilterLayer,QueryFilterNotEntity,QueryFilterRelationship,QueryHexNeighbors,QueryHexRange,QueryHexRing,QueryLine,QueryRectangle,TargetListGet}.json` |
| m12 | 测试名不副实：`EveryVignette_TicksOnce_WithChineseCaption` 既不校验中文，也不校验 `assertDetailContains`，只校验非空 + 无残留 `{}`；`ThinkWave_10k_AlwaysSuccess16_UnderFiveMilliseconds` 的实际门槛写的是 `< 15.0ms` | `GraphOpsNodeGalleryAcceptanceTests.cs`；`BehaviorTreeRuntimeTests.cs` |
| m13 | 无 board ⇒ `ApplyBoardSpatialConfig` 永不执行 ⇒ `SpatialQueryService._hexMetrics` 始终 null ⇒ 六角查询走默认值 fallback 分支，用 `HexCoordinates.EdgeLengthCm` 和浮点 `1.7320508f` 现算包围盒，与 `HexMetrics` 构成第二个 SSOT。（「无 Board 合法」本身成立，见 §4） | `src/Core/Spatial/SpatialQueryService.cs`；`src/Core/Engine/GameEngine.cs` |
| m14 | 画廊每 tick 对同一个 (viewer, subject) 写两次披露记录，两次 `observedTick` 取值还不一样（`0` vs `ResolveCurrentTick`），靠调用顺序才一致 | `…/Runtime/GraphOpsNodeActorBinding.cs`（`SyncHud` → `ApplyHealthBarKnowledge` 之后又 `SetHealthBarVisible`） |
| m15 | 「没亮的人不投影」的验收只断言 `KnowledgeProjectionStore` 里的掩码，没有断言 WorldHud 真的没发这根 bar；且**所有画廊验收都不调 `BindStageVisuals`**，`Stage == null` 时 `BindHud` 直接 return——玩家真正看到的那条路径（performer 绑定、血条、Knowledge 门控、`SyncHud`）零自动化覆盖 | `GraphOpsNodeGallerySpatialAcceptanceTests.cs`；`GraphOpsNodeActorBinding.BindHud`；`src/Tests` 全库无 `BindStageVisuals` 调用 |
| m16 | `T2` 的 `preset` 字段无任何校验器覆盖（`validate-registry.py` 只对 T1 记警告）；120 条 `acceptanceTest` 全是同一个类名、没到方法级，与覆盖表 `unitTestFilter` 的粒度不一致 | `scripts/validate-registry.py`；`showcase.registry.json` |
| m17 | `validate-registry.py` 强制 registry binding 与 launcher binding 双向一致，形成反向激励：退役条目一旦删掉 `binding` 就会校验失败，机制上鼓励「留着门」。正确先例是 `physics2d_playground`（退役时 binding / preset 双 null + 豁免） | `scripts/validate-registry.py` |
| m18 | 容量策略诊断文案对 Query 方言指错 Kind（写死 "on ControlFlow linear kinds"，但 `ValidateQueryNode` 的 `QueryRadius` 分支也调它） | `GraphControlFlowCompiler.Linear.cs` |
| m19 | `ConstTag` 是文档发明的节点，全仓不存在且无「另册」标注；四个死作者字段 `directionDeg` / `halfAngleDeg` / `lengthCm` / `halfHeightCm` 全仓无人读，作者填了会被静默丢弃 | `tag-display-lookup.md` §4.3；`GraphControlFlowDocument.cs` |
| m20 | 家族 mod 里 `PresentationAudienceRevealHidden` 被 `BindViewer` 永久置真且没人调 `GateWorldHudByKnowledge`，剔除/视野这一层被整体旁路（属性掩码仍生效，故不致命） | `GraphOpsStageVisuals.cs`（`BindViewer` / `GateWorldHudByKnowledge`） |
| m21 | `GraphOpsAttrRuntime.ResetCombatants` 裸写 `AttributeBuffer.SetBase/SetCurrent`，绕过 `AttributeMutationOps`、不打 dirty flag | `CapabilityStandardGraphOpsAttrMod/Runtime/GraphOpsAttrRuntime.cs` |
| m22 | 剧本/事件 driver 把已加载的 vignette 资产对象当运行时可写缓冲：`ctx.Vignette.Actors[i].X/Y` 被就地改写再由 `SetPosition` 推给表现层 | `Drivers/ScriptNodeDriver.cs`（`ApplyBeat`）、`Drivers/EventNodeDriver.cs`（`MoveMarker`） |
| m23 | 描边只有六角格是跟着实体 `WorldPositionCm` 画的；扇形、矩形、直线、仇恨线全部读 vignette 里写死的 `Actors[i].X/Y`，与实体脱钩 | `Drivers/SpatialNodeDriver.cs`、`LinearNodeDriver.cs`、`AttrNodeDriver.cs`、`RelNodeDriver.cs` |
| m24 | `docs/prd/*.html`、`docs/tdd/04-pipeline-design.html`、`docs/reference/api-quickref.html` 与 `docs/diagrams/{graph-compiler-flow,opcode-taxonomy}.svg` 仍把已删除的 `GraphCompiler` / `GraphValidator` 写成现役管线 | 上述文件；`src/Core/NodeLibraries/GASGraph/GraphCompiler.cs` 已不存在 |
| m25 | `Attr` 家族跑完 5 相进 `Phase.Complete` 后 `Tick` 永久早返回，场景冻成静帧，无循环无重置 | `GraphOpsAttrRuntime.cs` |
| m26 | `AggAverageAttribute` 与 `RelationshipAggAverageMetric` 的 summary 完全重复（「字幕报平均。」），是全表唯一一处重复文案；另有 10 条 summary 只有 3–5 个汉字（如 `AggMinAttribute` 的「最低值。」）。不违反 P2 禁令（零 opcode 名、零术语），但信息量参差 | `assets/Vignettes/{Op}.json` 的 `beat`（`summary` 由它单向生成，要改改分镜） |

### 3.4 债务

| # | 项 | 证据 |
|---|----|------|
| d1 | `RequireNoYield` 每次 `InvokeScript` 全量线性扫被调程序，O(n) 落在热路径；无分配但可在登记期一次性完成（#914 已记，仍在） | `GasGraphOpHandlerTable.cs` |
| d2 | 两个 Loader 抛裸 `InvalidOperationException`，未用 `GAS.*.ERR.*` 结构化码，与 `GraphKindOperationPolicy` 那套不统一 | `GraphFunctionCatalogLoader.cs`、`GraphActionCatalogLoader.cs` |
| d3 | `GraphRegistryTestBootstrap` 少一步 `ResolveFuncLibInvokes`，与生产加载顺序不完全同构；且它自行解析 `graphs.json` + `GraphIdRegistry.Register`，只有两个 catalog 走生产 Loader——M3「改走生产 Loader」只兑现一半 | `src/Tests/GasTests/Graph/GraphRegistryTestBootstrap.cs` 对照 `GameEngine.cs` 加载段 |
| d4 | FuncLib 只有 3 个条目，全部是 `ConstInt` → `HaltReturnInt`。两本清单的骨架和闸门都到位了，但 FuncLib 的复用面还只是个 demo | `assets/Configs/GAS/func_lib.json` |
| d5 | 换展厅完全不清：`TeamManager` 静态队伍关系、`RelationshipRuntime` 链接与反向索引、`EntityCollectionStore` 行、`KnowledgeProjectionStore`、`GameplayEventBus`、`EffectRequestQueue._budgetFused` / `_nextRootId`、`GameEngine._simulationBudgetFused`。键含 `Entity.Version` 所以不串人，但死行只增不减，`RelationshipReverseIndex.Compact()` 在展厅路径无人调用 | `GraphOpsHeadlessGameEngine.LoadExclusiveMap`；`src/Core/Association/EntityKeyedSoaTable.cs` |
| d6 | 8 次步进上限挂在四个可被外部改动的全局上（`Time.FixedDeltaTime` / `Time.TimeScale` / `SimulationBudgetMsPerFrame` / `Pacemaker`）；一旦 `_simulationBudgetFused` 熔断，此后每次开图都抛误导性的 "must Start() the engine" | `GraphOpsHeadlessGameEngine.AdvanceUntilMapActorsAreSpatiallyIndexed` |
| d7 | 两个清理循环用 `World.Query(in q, lambda)` + `List<Entity>`，每次开图 2 次委托分配 + 2 个 List，非 chunk 迭代。Core 的 `MapSession.Cleanup` 同写法，故记债务而非违规 | `GraphOpsHeadlessGameEngine.cs` |
| d8 | mod 侧直写引擎全局：`spatialQueries.SetCoordinateConverter(...)`、`TeamManager.SetRelationship(1, 2, Hostile)`（静态、跨展厅跨地图不复位） | `…/Runtime/GraphOpsNodeGalleryHost.cs` |
| d9 | 死 API：`GraphOpsHeadlessGameEngine.Create` 已无外部调用者；`GraphOpsEngineWorld.StartOwnedAndTick()` 是空方法却仍被家族调用；`startPath` 参数被 `_ = startPath;` 丢弃 | `GraphOpsHeadlessGameEngine.cs` |
| d10 | Float / Blackboard 家族的图以 C# 字符串常量内嵌在程序集里（两个 mod 都没有 `assets/GAS/`），成为配置管线、mod JSON 之外的**第三个图来源**；Float 还每个 wave 从 JSON 重编译两张图 | `GraphOpsBlackboardGraphAuthoring.cs`；`GraphOpsFloatRuntime.cs` |
| d11 | 八家族约 2.9k 行 runtime + 八个 csproj 仍被 `GasTests` 引用，每次 CI 编译并跑；但 fixture 断言的大多是家族 runtime 自己攒的中文文案和自己维护的计数器，不是引擎行为。真正有含金量的只有 Spatial 的 `AssertTargetListGetOnAllGraphs` 与 Script 的 yield/halt 时序，两块都可搬进 per-op 画廊或 Core 图测试 | `src/Tests/GasTests/GasTests.csproj`；`src/Tests/GasTests/Production/GraphOps*ShowcaseAcceptanceTests.cs` |
| d12 | 120 间展厅里 60 间是同一对人（施法者 + 木桩）、117 间用完全相同的 `DefaultCamera`，6 间只有 1 个人。连着点十几间视觉高度雷同，变的主要是字幕和地上多出来的圈 | `assets/Maps/*.json` 人数分布 1×6、2×60、3×4、4×2、5×17、7×2、8×6、13×23 |
| d13 | 分镜 title/beat → `showcase.registry.json` 的 title/summary → 入口 mod `game.json` 的 `windowTitle` 三处之间没有 parity 测试；生成器只在 `GENERATED.txt` 里写「Do not hand-edit」，两个根 JSON 连标记都没有，而启动器运行时会整文件回写它们 | `scripts/generate-graph-op-node-galleries.py`；`LauncherService.cs`（`SavePresetSelectors` / `SaveRepoConfig`） |
| d14 | 生成器 `--strict` 是死开关：不加它也会在「覆盖 op 无分镜 driver」处 `SystemExit`，`missing` 分支永远走不到 | `scripts/generate-graph-op-node-galleries.py` |
| d15 | 覆盖表最强的 op 专属证明 `ExistingVignettes_CompileWithFeaturedOp`（唯一断言编译产物真的 emit 了 featured opcode）和 `GeneratedMaps_SpawnEveryVignetteActor` 在 120 条 filter 里**引用次数为 0**；`unitTestFilter` 也不是可运行的 filter 表达式（分号拼接的裸 `Class.Method`，生成器还主动剥掉 `FullyQualifiedName~` 前缀），字段名与内容不符 | `assets/Configs/GAS/graph_node_op_coverage.registry.json`；`GraphOpsNodeGalleryAcceptanceTests.cs` |
| d16 | 文案质量无守卫：`AssertBannedPlayerCopy` 只作用于运行时 `Metrics.Detail`，不校验 `showcase.registry.json` 的 title/summary。P2 的「禁止只堆 opcode 名」目前靠人守，可无声退化 | `GraphOpsNodeGalleryAcceptanceTests.cs` |
| d17 | 火→冰缺端到端验收：没有任何测试加载 `lsw_hot_apply_arena`、驱动 `LswChampionHotApplyDemoSystem`、断言第二次施放命中后目标掉 45 血并挂 `State.LSW.Chilled`。现有两条测试各覆盖一半且都不过引擎 | `src/Tests/GasTests/LiveSkillWorkbench/LswFireToIceHotApplyTests.cs` |
| d18 | LSW Vignette 那条路的「施放/结算」是假的：`OnProjectileImpact` 把 `_lastReturnInt / 100f` 直接减到本地 `float _dummyHp`，无 Effect、无 GAS 结算、无投射物实体；「冰」只是 `_projectileFrost = true` 颜色标记。热改本身是真的。（不是 Champion 路径，Champion 那条是干净的） | `LiveSkillWorkbenchVignetteRuntime.cs` |
| d19 | 既有债，未回归红线：Champion 火→冰真机录屏 / Xvfb SIGSEGV；#615 Save/UI 尾巴 | `pr895_graph_infra_and_lsw_architecture_audit.md`、`pr911_audit_fix_checklist.md` §5 |

---

## 4. 场景（玩家看见什么 vs 实际）

### 4.1 打开启动器

**看见：** 顶栏一个预设下拉，169 个预设按名字列着；中间 271 张卡按 T1–T4 分组。没存过偏好的新玩家预选的是名字排最前的 `Animation Acceptance Raylib`，和图无关。搜 `graph_op` 翻到 120 张中文标题卡。

**实际：** 没有硬编码默认关——全仓 `src/Tools/` 下搜 `capability_standard_graph_op` 零命中，下拉直接映射 `launcher.presets.json`。**这条声称成立。**

**但：** 同一个列表里躺着 8 张打灰徽章的退役家族卡，卡底照样有可复制的启动命令，因为 `launchHint()` 先读 `binding` 且不看 `status`。玩家复制粘贴就能把「已经关了」的门重新打开，界面上除了一枚灰徽章没有任何东西拦他（B2）。

### 4.2 点「两段伤害叠在一起」

**看见：** 施法者和木桩两个人，左上角黑底黄框字幕带写着「基础伤害加上额外伤害，这一刀一共 42；血条从 100 掉到 58。」，血条跟着掉。

**实际：** 人真是这张地图刷的——`BindMapActors` 只从 `engine.CurrentMapSession.EntityIndex` 按 `InstanceId` 认领，认不到当场抛，画廊 mod 里 `World.Create` 零命中。字幕模板真在 `Vignettes/AddFloat.json`。`42` 真是图算的（真 VM、真寄存器）。**这三条声称都成立。**

**但：** `100 → 58` 这一步是 C# 干的——`next -= 42` 然后 `AttributeMutationOps.SetCurrent` 直接改属性，不经任何效果结算；而且如果这一刀本该把血打到 0 以下，代码会静默把它回卷成开局的 100（B3）。

### 4.3 再点一间圈人展厅

**看见：** 施法者周围亮起一圈，字幕报「摸到 4 个近处的人」，圈外的人暗着但还站在场上。

**实际：** 上一间的人不会留下——`LoadExclusiveMap` 是 `UnloadMap` → 清效果队列 → 硬销毁挂起的表现实体 → `LoadMap` → `ClearPartition` + 全世界摘 `SpatialCellRef` → 定长步最多 8 拍等到全部演员进格，等不到就抛。**「换展厅不留人」和「真的等到空间索引就绪」两条声称都成立**，而且是定长步不是推一拍就走。圈外人的语义也干净：世界血量仍是作者数据 100，只是 HUD 不披露，有测试逐档断言。

**但：** 清效果队列那一步没真清——`EffectRequestQueue.Clear()` 会把 overflow 环里的旧请求灌回主缓冲（M1）；清分区排在 `LoadMap` 之后，整个 `LoadMap` 跑在脏分区上（M23）。

### 4.4 「没亮的人」

**看见：** 暗着的人头顶没有血条。

**实际：** 这条是真的，而且走的是正式管道：`graphops.hud.health_bar` 继承 `AttributeBinding(Health)`，`WorldHudPerformBehavior` 按 `KnowledgeProjectionStore` 的属性掩码决定发不发，`PresentationPresence` 仍是 `LiveVisible`。人还在、位置照常披露、世界血量原样，只是 Health 从掩码里摘掉。**声称成立。** 决定「谁亮」的是 C# 的 `ActorHudLit` 数组（分镜导演式点亮），这对展厅是合理的。

### 4.5 策划删掉动作清单 / 写成空表

**看见：** 启动就报错并点名路径。

**实际：** 成立，且两个 Loader 逐字对称：目录未声明该路径 → `Config catalog must explicitly declare '{path}'`；声明了找不到文件 → `'{path}' is declared in catalog but no file was found.`；合并结果为空 → `'{path}' merged to an empty catalog.`。两者都在方法第一行 `_catalog.Clear()`，`catalog == null` 时抛 `is mandatory`。测试 `FuncCatalogLoader_RejectsMissingFile` / `RejectsEmptyArray` / `ActionCatalogLoader_RejectsMissingFile` / `RejectsEmptyArray` 全绿。**声称成立，没有默认空表旁路。**

### 4.6 纯算式里跳进「等一拍」

**看见：** 登记/热改当场拒绝。

**实际：** 成立，而且是真闭包遍历不是单程序扫描：`GraphYieldPurityValidator` 沿 `InvokeScript`（`functionName` 与 `graphId` 两条形态都走）和 `Call` / `Jump` / `JumpIfFalse` 递归，越界目标和未知 opcode 都判 fail。五条测试守着：`RejectsYieldProgram`、`RejectsPureScriptCallTargetClosureToYield`、`RejectsPureScriptInvokeScriptFunctionNameClosureToYieldBeforePatch`、`RejectsPureScriptInvokeScriptGraphIdClosureToYield`、`Classify_FuncLibGraphBodyReplaceThatReachesYield_MapReloadRequired`。**声称成立。**

**但：** 反过来那一侧没关严——作者可以从 Query 图里用 `graphId` 直接调起 ActionLib 动作（B1）。

---

## 5. 边界

### 5.1 刻意不审

UI 面板图（#886 / #893 及查表 / TagDisplay 面板债）、表现层改名 / 贴花 / 客户端座椅、更早平行的 GraphScore 预算 [#723](https://github.com/MightyBubble/Ludots/pull/723)。本报告未把这三项写进任何阻断或 Major。

### 5.2 已裁决共识（本轮未重开）

Duration / Period 在效果壳上；FuncLib 纯、ActionLib 可挂起、同名跨库失败关闭；Effect 可分支 + 调 FuncLib、不得调 ActionLib；一种作者边模型 + 一台 VM；图节点玩家门是单节点展厅、八家族退役；人从地图刷、血条走 WorldHud / 生命披露；NO FALLBACK。

本报告的三条阻断都是**按这些共识去量**得出的，不是对共识的挑战。

### 5.3 已知已知的核实结果（不按旧红灯复读）

| 项 | 核实结果 |
|----|----------|
| 合同「修复中」 | **确认仍是修复中**，且**必须继续是修复中**（见 §7）。全量扫 `gitbook/**/*.md`，没有任何文档把 FuncLib/ActionLib 合同写成已落地 |
| 旁路票代码已叠进 main | **三张全绿，不要按旧红灯复读**。`MathOpsChain_Stress_ZeroAllocation` 通过（#917 原为 ~880KB alloc）；`Graph.Shield.Absorb` 的 `LoadAttribute` 值边现在是 `toPort: "source"`，`AuditCoverageTests` 全绿（#916）；`GasTests` 干净构建 0 Error，MSB3021/3027 不复现（#918） |
| GitHub issue #915–#918 | 票面状态与代码状态不一致，代码侧已进。本报告按代码判，不按票面判 |
| docs-governance | **仍红**，且能点名规则与断点（M13）。不是「CI 红」而是 `missing-link-target`：`gitbook/acceptance/graph-funclib-actionlib-uat.md` 第 7 行少一个 `../` |

### 5.4 一处需要维护者知道的取证外因

需求书 `docs/audits/pr932_graph_landed_audit_handoff.md` **不在被审 tip `82ddb3322a` 上**（它在 PR #939 的分支 `d8c2d105d`），九个领域审计员都独立发现了这一点。各领域用合同页 + `pr911_*` 三份文档作为判据基线，结论不受影响。

---

## 6. 本轮取证（可复现）

全部在 `origin/main @ 82ddb3322a` 上实跑，工作区无生产代码改动。

| # | 动作 | 结果 |
|---|------|------|
| 1 | `dotnet build src/Tests/GasTests/GasTests.csproj -c Debug` | **0 Error(s)**，2299 Warning(s)，51.31s |
| 2 | `dotnet test src/Tests/GasTests/GasTests.csproj --no-build` | **Passed: 2453, Failed: 0, Skipped: 0**，8m29s |
| 3 | `GraphNodeOp` 枚举 ⊖ 覆盖表 | 枚举 121（含 `None`），覆盖表 120，**「枚举减 None」== 覆盖表集合**，双向差集 0 |
| 4 | 610 个 `unitTestFilter` token 的 `Class.Method` 归属 | **610/610 解析成功**（类、方法、类-方法归属全部命中）。语义层的 21 条错误归因见 M3 |
| 5 | 120 条 per-op 展厅条目的引用文件 | `path` / `docsPath` / `screenshot` / `video` **零悬空**；120 张 poster.png 互不相同（74–100 KB）；120 段 play.mp4 是 LFS 指针且 oid 互异 |
| 6 | **生成器零漂移**：镜像到 `/tmp` 后 `python3 scripts/generate-graph-op-node-galleries.py --strict` 复跑再 diff | `launcher.config.json` / `launcher.presets.json` / `showcase.registry.json` / `graph_node_op_coverage.registry.json` **逐字节相同**；120 个薄入口 Mod、画廊分镜与地图**全部相同**。「生成器是唯一写入源、禁止手改」这条**经验证成立**（但无 CI 闸门保证，见 M18） |
| 7 | `action_lib.json` 11 个条目逐个查图 | **只有 `bt.patrol` 与 `script.drinkUntilFull` 含 Yield**，其余 9 个不含（B1 的可攻击面）。FuncLib 3 条全部 `purity=pure` 且只有 `ConstInt` → `HaltReturnInt` |
| 8 | `python3 scripts/validate-registry.py` | **PASS**：271 条、177 binding、0 错误、23 警告（全是非图条目的 T1 screenshot 待补） |
| 9 | `pwsh ./scripts/validate-docs.ps1` | **FAIL**，`missing-link-target`（M13） |
| 10 | 合同失败关闭守卫 | 18 条全绿：两个 Loader 的 missing-file / empty-array、`RejectsNonPurePurity` / `RejectsScoreKind` / `RejectsYieldProgram`、三条间接 Yield 闭包、`RejectsNonScriptKind`、`RejectsFuncLibNameClash`、`FrontDoor_RejectsLegacyNextChain` |
| 11 | Effect 前门表达力与边界 | 全绿：`FrontDoor_EffectBranchBool_CompilesToJumps`；`FrontDoor_EffectWaitYieldAndLoopSugar_FailClosed`（Wait/Yield/While/Until/SwitchInt 五个）；`FrontDoor_EffectInvokeScriptFunctionNameActionLibName_PatchFailsClosed`；`FrontDoor_LinearKindsInvokeScriptGraphId_FailClosed`（Score/Validation/Derived）；`FrontDoor_NonEffectLinearKindsRejectBranchBool` |
| 12 | L2 跨拍与热改 | 全绿：`PatrolYield_ResumesAcrossThinkWaves_ThenReturnsPatrolIntent`（真跨拍续跑）；`Classify_UnknownGrantedTag_RequiresEngineRestart_DoesNotRegister`；`Classify_UnknownAttribute_RequiresEngineRestart`；`CommitNextCast_PartialFailure_RollsBackAllCandidates`；`HotApply_FireboltToIcebolt_SwapsImpactPresentationAndDamage` |

原始输出见 PR 附带的 `audit_verification_log.txt`。

---

## 7. 合同符合性与状态判定

### 7.1 §3.3 FuncLib

| 条款 | 判定 |
|------|------|
| 资产字段 / `purity` 默认 pure、非 pure 拒绝 | 符合 |
| 允许 kind 仅 `Script`(pure)，Score/Validation 延后 | 符合（延后 = 真拒绝；全仓无 `InvokeScore` / `InvokeValidation`） |
| 加载后校验目标图无 Yield/Wait（含间接） | 符合 |
| `GraphKindOperationPolicy` 与 purity 一致 | 符合（登记期统一执行，Loader 不重复做，符合 DRY） |
| 未登记名失败关闭 | 符合 |
| 作者节点二选一、禁两套名并存 | 符合（只有 `InvokeScript.functionName`） |
| **所有 L1 Kind** 前门白名单含该调用节点 | 符合 |
| 被调图一次 RunToHalt、禁嵌套 Yield、CallStack 调用方自备 | 符合 |
| **I/O 值边 / 寄存器约定 SSOT** | **违反**（M14，无任何文档） |
| **`readsBlackboard` 显式声明、默认 false** | **违反**（M14，字段不存在） |

### 7.2 §3.4 ActionLib

| 条款 | 判定 |
|------|------|
| 新资产 `GAS/action_lib.json`、`kind=Script` | 符合 |
| 目标图允许 Yield、允许副作用 | 符合 |
| 不得与 FuncLib 同名 | 部分（名字层守住，同一 graphId 双注册无守卫 — m3） |
| **作者节点 `InvokeAction`** | **违反（未实现）**。全仓无此 opcode / 节点，只在三份 md 里出现。§3.3 给了「二选一」的替代授权，§3.4 没给 |
| **Effect/Score/Validation/Query/Derived 前门不得出现 Action 调用** | **违反（Query）** — B1 |
| L2 解析只走 ActionLib 或 Registry、禁私藏程序宇宙 | 部分（五个 Showcase + AI 测试全走 `RequireActionId`；`GraphOpsScriptRuntime` 一处 `RequireId` 字符串旁路 — m1） |
| 续跑由宿主持有 cursor + 寄存器 | 符合（`bt.patrol` 实测含 Yield，跨拍续跑有测试；M5 真关了） |
| §4.4 场景「HFSM OnTick 挂含 Yield 的 ActionLib」 | **违反**（实现禁止，且加载期不按宿主校验 — M11） |

### 7.3 §3.5 Effect 表达力

| 条款 | 判定 |
|------|------|
| A. Effect 线性白名单含 FuncLib 调用 | 符合 |
| B. Effect 允许 `BranchBool`，仍禁 Wait/While/Yield | 符合（`BranchBool` 是作者糖降级成 `JumpIfFalse` + `Jump`，非 `GraphNodeOp` 成员；五个 op 一起被测试锁住） |
| 禁止 Effect 内 While+Wait 模拟 Period | 符合 |

### 7.4 §5 边界

| 边界 | 判定 |
|------|------|
| 禁止平行第二套 VM / 作者边模型 | 符合（`GraphCompiler.cs` 已不存在，`GraphControlFlowCompiler` 单一入口；画廊编译走 `GraphProgramAuthoringFrontDoor.CompileJsonObjectFull` 薄封装） |
| 禁止 Effect 事务中途 Yield / 调 Action | 符合（Effect 侧四重闭合：前门 + patch + 加载期 + 运行期） |
| 禁止 FuncLib 含 Yield 或未声明即产生副作用 | 部分（Yield 侧闭合；副作用侧只靠 Script kind 的 `Pure` metadata，`readsBlackboard` 未实现） |
| 禁止同名；禁止缺文件/空表静默 | 部分（空表/缺文件符合；同名只到 name 层） |
| 禁止用 ActionLib 替代 Duration/Period | 符合 |
| 禁止编译期文本 Macro 展开 | 符合（`PatchFuncLib` 只改 `ins.Imm` + 清 flag，无内联） |
| L2 不得私藏 Dictionary 程序宇宙 | 部分（无本地程序字典；一处字符串旁路） |
| 热路径 0-alloc；CallStack 调用方自备 | 部分（`HandleInvokeScript` 全 `stackalloc` 无堆分配；`RequireNoYield` 每次全量扫 — d1） |

### 7.5 §6 UAT 逐条

| 合同 §6 场景 | 判定 | 依据 |
|--------------|------|------|
| 用模板字段而不是 Yield 定义 DoT | **过** | Effect 禁 Yield 四重闭合；Period 在 `EffectLifetimeSystem` 壳上 |
| Effect 阶段调用 FuncLib | **过** | `FrontDoor_EffectInvokeScriptFunctionName_CompilesAndPatchesViaFuncLib` |
| 含 Yield 的图不能进 FuncLib | **过** | 五条闭包纯度测试全绿 |
| 行为树叶子调用 ActionLib（跨拍续跑） | **过** | `bt.patrol` 实测含 Yield；`PatrolYield_ResumesAcrossThinkWaves_ThenReturnsPatrolIntent` 三拍断言 |
| 技能阶段不能调用 ActionLib | **未过** | 场景原文用 `InvokeAction`，该节点不存在，**无法照字面测**；等价守卫存在且绿（`FrontDoor_EffectInvokeScriptFunctionNameActionLibName_PatchFailsClosed`），但**同一红线在 Query 方言上是开的**（B1） |
| Score 图产出分数且可调 FuncLib、不 Yield、无副作用 | **过** | `FrontDoor_LinearKindsInvokeScriptFunctionName_Compile("Score")`；`GraphKindOperationPolicy` |

### 7.6 合同状态：**必须继续「修复中」**

四条硬理由，任何一条单独成立就不能改回「已落地」：

1. §3.4 的作者节点 `InvokeAction` 未实现，且 §3.4 的前门禁令在 Query 方言上是开的（B1）。
2. §3.3 的 I/O SSOT 与 `readsBlackboard` 两条完全未实现（M14）。
3. §4.4 的 HFSM Yield 场景与实现直接冲突（M11）。
4. §6 有一条 UAT 无法照字面执行（§7.5）。

外加：合同的验收页自身 SSOT 破裂（M12）且让 docs-governance 在 main 上持续红（M13）。

---

## 8. 与 #914 审计的衔接

### 8.1 已关（不要复读）

| #914 / 清单项 | 现状 |
|---------------|------|
| B1 两个 Loader 缺文件 / 空表失败关闭 | **关**，18 条守卫全绿 |
| B2a FuncLib 间接 Yield 可达性（含 `graphId`） | **关**，真闭包遍历 |
| B2b LSW 热替换含 Yield 拒绝、传真实 catalog | **关**（`_functions` 构造必填），但缺反向依赖扫描（m4） |
| B3 Arena 门槛还原 | **关**，全量套件绿 |
| M1 非 Script FuncLib kind 加载拒绝 | **关** |
| M2 `GraphActionCatalogLoader` FuncLib 参数必填 | **关**，四个参数全 `?? throw` |
| M3 测试引导改走生产 Loader | **部分关**（d3，仍自行解析 `graphs.json`、少一步 `ResolveFuncLibInvokes`） |
| B4 / P1 44 个无前门 opcode 补回 | **关，缺口 0**。三层集合（白名单 / validate / compile）差集为空，且每个 opcode 有一张真编译真跑的授权图 |
| P1 被删守卫测试恢复（含 `SpatialQueryIncomplete`） | **关**，六条全回到 `GraphControlFlowConfigCoverageTests` |
| M4 合同 §6 UAT 映射页 | **形式上关，实质破裂**（M12 两个 H1 互相矛盾 + M13 让 CI 红） |
| M5 至少一条 `bt.*` 真 Yield + 续跑验收 | **内核关、Showcase 未关**（M24 自证循环） |
| Query L1 纳入 FuncLib 调用 | **关** |
| P3「线性 `graphId` 拒绝 / Effect→ActionLib 名补测试」 | **实质已关，清单勾选滞后**。两条测试都在树上；剩余缺口是参数列表漏 Effect 与 Query（m6） |
| 加载顺序文档偏差 | **关**，合同 §3.7 与分层页现在与 `GameEngine` 加载段逐字一致 |
| 旁路票 #916 / #917 / #918 | **三张全关**（见 §5.3） |

### 8.2 仍开

`RequireId` 死 API（**且已从「死」变「活旁路」**— m1）、硬编码 ScriptKeys（m7）、旧 `GraphCompiler` 文档图（m24，`gitbook` 侧已清、`docs/*.html` 与两个 SVG 仍在）、`RequireNoYield` 热路径全量扫（d1）、合同状态不得改回已落地。

### 8.3 新债（#914 之后新增或此前未记录）

B1（Query 前门开口）、B2（退役门未锁 + 五处 `GraphIdRegistry.Clear`）、B3（血条数值 C# 摆放 + 静默回卷）、M1（`Clear()` 不真清）、M2（`InvokeBuiltin` 无界 materialize）、M3 / M4（覆盖表错误归因 + 守卫闭环自证）、M6 / M7（`AllowTruncated` 与 `validOutput` 写不出来）、M8（tag-display 文档门面不可作者化）、M9（热改会话层非原子）、M10 / M11（L2 调不到 FuncLib；HFSM Yield 与合同冲突）、M17（静态注册表跨引擎污染）、M18（生成器只 upsert 不删除 + 无 CI 闸门）、M19（sandbox 第二套展示人）、M25（LSW 登记表验收映射失真）。

---

## 9. 给修复 Agent 的提示词（按领域拆，不要合成一条）

每条独立开一个 Agent。共同纪律：先读 `gitbook/contributing/ai-assisted-development.md` 的任务执行决策规范；NO FALLBACK / SSOT / DRY；只改自己那一条，不顺手改别的；不重开已裁决产品争论。

### 9.1 修 B1（合同红线，优先级最高）

```text
只做一件事：关掉 Query 方言的 ActionLib 开口。
对象：src/Core/NodeLibraries/GASGraph/GraphControlFlowCompiler.Query.cs。
现状：ValidateQueryNode 的 InvokeScript 分支只拒绝「两个都给」和「两个都不给」，
只给 graphId 一路放行，CompileQueryNode 原样编译成 Imm = node.GraphId; Flags = 0。
线性方言 GraphControlFlowCompiler.Linear.cs 的同名分支是正确参照：graphId 一律报
TypeMismatch「cannot use graphId in linear FuncLib authoring」。
要求：
1) Query 方言与线性方言对齐：InvokeScript 只允许 functionName，graphId 直绑拒绝，
   诊断码与文案沿用线性侧，不要发明新错误码。
2) 给 GraphEffectAuthoringExpressivenessTests.FrontDoor_LinearKindsInvokeScriptGraphId_FailClosed
   的 [TestCase] 补上 "Effect" 与 "Query" 两个 kind。
3) 若因此打破了任何现有 Query 图（先搜 assets/ 与 mods/ 下 kind=Query 且用 graphId 的图），
   失败关闭并报告，不要为了让测试绿而放宽校验。
禁止：改合同措辞来迁就实现；新增 opcode；碰线性方言。
```

### 9.2 修 B2（玩家门 + 核心注册表）

```text
只做一件事：把八间退役家族展厅的门真锁上，并停止清空引擎图编号表。
第一步（锁门）：
1) showcase.registry.json 里八条 capability_standard_graph_ops_* 的 binding 置 null
   （preset 已经是 null）。参照正确先例 physics2d_playground。
2) launcher.config.json 删掉对应八条 binding。
3) scripts/validate-registry.py 强制 registry binding 与 launcher binding 双向一致，
   所以要按它的 exemptions(kind=binding) 机制补豁免，或改规则让 retired 条目允许 binding=null
   —— 二选一，写清理由，不要留静默特例。
4) 启动器侧：src/Tools/Ludots.Launcher.React/src/lib/showcase.ts 的 launchHint 与
   components/ShowcasePanel.tsx 必须让 status=retired 不再产出可复制启动命令。
第二步（停止清表）：
删掉这五处生产代码里的 GraphIdRegistry.Clear()：
  GraphOpsRelShowcaseBootstrap.cs / GraphOpsQueryCatalogBootstrap.cs /
  GraphOpsAttrGraphBootstrap.cs / GraphOpsSpatialCatalogBootstrap.cs /
  GraphOpsEventGraphBootstrap.cs
Rel 与 Query 的 ModEntry 还在 GameEvents.GameStart 上对活引擎调
BindStandaloneFromModAssets() —— 这条也要断掉。
清表是测试夹具的需求，不是 mod 运行时的需求；测试侧需要就在测试里做，并给这六个
未标注的 fixture 补 [NonParallelizable]（只有 Rel/Query 标了）。
禁止：删掉八家族 Mod 本身（那是另一张票）；改动 per-op 画廊。
```

### 9.3 修 B3（血条名实）

```text
只做一件事：让血条名实相符，并删掉静默回卷。
对象：mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/Runtime/。
问题一（红线）：LinearNodeDriver 有 `if (next <= 0f) next = opening;`，
EventNodeDriver 也有同类回卷。本该「打死了」的时刻被静默改写成满血重来，
且这条规则不在任何分镜数据里。要么删掉让它失败关闭，要么把「循环/复位」
写成分镜里的显式字段（推荐后者，data-driven），禁止留在 C# 里。
问题二（共识 6）：数值来源是真图（保留），但落地是 AttributeMutationOps 直接写属性，
不经效果结算。分两类处理，不要一刀切：
  - 真有结算语义的 op（ApplyEffect* / FanOutApplyEffect* / ModifyAttributeAdd /
    WriteSelfAttribute 等）：走正式效果管线，让血条反映真结算。
    参照 AttrNodeDriver.Tick 那条已经干净的路（ExecuteFeaturedGraph + SyncActorHealthFromWorld）。
  - 纯算式 op（AddFloat / ClampFloat 之类，图本身不改世界）：血条不是它的正确表达。
    要么改用非血量的指示物，要么在分镜与 showcase summary 里说清「这根条是示意」。
    今天 showcase.registry.json 的 AddFloat summary 写「血条按总和往下掉」，
    玩家会理解成真结算 —— 文案与实现必须对齐，改哪边都行但必须一致。
问题三：ScriptNodeDriver 把「茶水量」写进 ActorHealth，属于血条被当通用数值槽复用，
同上处理。
禁止：动 Knowledge / WorldHud 披露管道（那一侧是对的，别改坏）；
      改测试门槛来掩盖；把 ctx.ActorHealth 影子数组的读写方向搞反。
```

### 9.4 修 M1 + M23（换展厅清理正确性）

```text
只做一件事：让换展厅真的清干净，顺序正确。
对象：mods/showcases/capability_standard/CapabilityStandardGraphBehaviorCommon/GraphOpsHeadlessGameEngine.cs
      与 src/Core/Gameplay/GAS/EffectRequestQueue.cs。
1) EffectRequestQueue.Clear() 现在是 `_count = 0; RefillFromOverflow();` ——
   把 overflow 环里的旧请求又灌回主缓冲，_budgetFused / _nextRootId / _dropped 都不复位。
   Clear 的语义应当是真清空（含 overflow 环与熔断位）。注意它是 Core 类型，
   先搜全部调用方确认没人依赖「Clear 后 refill」这个行为；若 ConsumePrefix 需要
   refill 那是它自己的事，不要让 Clear 复用。
2) LoadExclusiveMap 的步骤顺序：现在是 Unload → 清队列 → 硬销毁 → LoadMap → ResetSpatialIndex。
   清分区必须排在 LoadMap 之前，否则整个 LoadMap（MapLoaded 触发器、
   ParticipantBindingResolver、LoadPathingForSession）都跑在脏分区上。
3) DestroyPendingPresentationActors 直接 World.Destroy，绕过引擎 publish→finalize
   两拍销毁协议，且全世界无差别扫。改走正式协议，并只扫当前 map。
4) 补一条守卫测试：连开两间圈人展厅，断言第二间的 TargetList 里没有第一间的人，
   且字幕报的人数等于亮着的人数。今天全仓没有任何测试引用 LoadExclusiveMap。
禁止：为了让测试绿而放宽 AdvanceUntilMapActorsAreSpatiallyIndexed 的失败关闭。
```

### 9.5 修 M3 + M4（覆盖表守卫）

```text
只做一件事：让覆盖表的 covered 是被度量出来的，不是被结构保证的。
对象：src/Tests/GasTests/Graph/GraphNodeOpCoverageRegistryTests.cs
      与 scripts/generate-graph-op-node-galleries.py。
1) LoadGasTestMethodNames 只按方法名建集合、丢掉类名，所以「filter 指向的测试
   不在它声称的类里」抓不到。改成 (类, 方法) 对。
2) 更重要：守卫要校验「被引用的测试真的执行该 op」。今天 event 家族 15 个 op 全指向
   SnapToNearestInCollection_SucceedsWithPlayerCaption（单 op 测试），
   ConstFloat / AddFloat 指向 FloatFamilyOp_RendersPlayerCaption 而那个
   TestCaseSource 数组里没有它们 —— 共 21/120 条错误归因。
   守卫需要能展开 [TestCaseSource] / [TestCase] / 方法内 op 数组字面量。
3) 生成器的 DRIVER_FAMILY_TEST 按 driver 字段盲配，是错误归因的产生机制。
   改成：只有该 op 真在目标测试的 op 列表里才写进 filter；否则指向它的
   op 专属测试（很多已经存在，只是没被引用），都没有就让生成器失败关闭。
4) hasGalleryTest 只查前缀，而那两条 token 是生成器无条件注入的 —— 生成器写什么
   守卫就查什么，这条断言永远不可能失败。改成要求至少一条 op 专属画廊测试。
5) 顺手把 ExistingVignettes_CompileWithFeaturedOp（唯一断言编译产物真的 emit 了
   featured opcode 的测试）和 GeneratedMaps_SpawnEveryVignetteActor 纳入 filter，
   今天它们引用次数是 0。
禁止：为了让守卫绿而把 21 条改成 status=missing 了事 —— 那 21 个 op 大多有真测试，
      问题是指针指错了；先修指针，确认有 op 真的没有专属测试再谈降级。
```

### 9.6 修 M13 + M12（docs-governance 与验收页 SSOT）

```text
只做一件事：让 main 的 docs-governance 变绿，并把验收页合成一份。
1) gitbook/acceptance/graph-funclib-actionlib-uat.md 第 7 行的链接
   architecture/graph-funclib-actionlib-contract.md 少一个 ../，
   相对解析成了 gitbook/acceptance/architecture/...（不存在）。
   规则是 scripts/validate-docs.ps1 的 missing-link-target。
   改完用 pwsh ./scripts/validate-docs.ps1 复跑确认整个仓库零 finding。
2) 同一个文件里有两个 H1，是两份文档被拼接，对同一批合同 §6 场景给出互相矛盾的
   覆盖结论（一处说已覆盖、一处标 Gap）。合成一份，逐条以实测为准：
   - 「bt.patrol resume」已覆盖（PatrolYield_ResumesAcrossThinkWaves_ThenReturnsPatrolIntent），
     下半那句「缺一条 bt.patrolStep 真 Yield」已不成立。
   - 「技能阶段不能调用 ActionLib」：合同原文用 InvokeAction，该节点不存在，
     真正存在的守卫是 FrontDoor_EffectInvokeScriptFunctionNameActionLibName_PatchFailsClosed，
     且同一红线在 Query 方言上还是开的（见修复 9.1）。照实写，不要写成已覆盖。
   - 合同 §6 写 bt.patrolStep，资产实际叫 bt.patrol，名字要对齐（改哪边都行，一处为准）。
3) gitbook/SUMMARY.md 指向的是被删掉的下半标题，同步更新。
禁止：删场景来让映射表好看；把「等价守卫存在」写成「合同原文场景已覆盖」。
```

### 9.7 修 M6 + M7（作者前门名实）

```text
只做一件事：让文档写得出的空间查询字段真的写得出，或让文档说实话。
对象：src/Core/NodeLibraries/GASGraph/GraphControlFlowCompiler.Linear.cs
      与 gitbook/architecture/graph-layering-flow-and-behavior.md。
1) RequireSpatialCapacityPolicy 在策略合法且 droppedOutput 已填时仍无条件报错
   「not yet authorable on ControlFlow linear kinds」，两个方言的 compile 对全部
   空间 op 硬写 Flags = 0 —— 没有任何输入组合能产出 Flags = 1。
   而分层文档写的是「AllowTruncated 必须同时声明 droppedOutput（未声明则失败关闭）」，
   读者会以为声明了就能用。二选一：真做成可作者化（含 droppedOutput 的消费路径 +
   一条 AllowTruncated 的正向测试），或把文档改成「暂不可作者化」并说明何时开放。
   不要留「守卫恢复了、前门字段没恢复」这种半成品状态。
2) validOutput 被前门接受但写进共享 scratch B[31]
   （GraphVmLimits.MaxBoolRegisters - 1），TargetListGet 无条件也写 B[31]，两者相撞；
   而 SnapToNearestInCollection 的输出端口只放行 value，那个 bool 没有任何值边能读到。
   守卫测试 SnapWithValidOutput_AllocatesDedicatedBoolRegister 现在是绿的，但
   "Allocates" 与 "Dedicated" 都不成立。要么真分配专用寄存器 + 开放端口，
   要么拒绝该字段；测试名要与行为一致。
3) 诊断文案写死 "on ControlFlow linear kinds"，但 ValidateQueryNode 的 QueryRadius
   分支也调它，Query 作者会收到指错方言的诊断。
禁止：改测试断言去迁就现状而不改行为或文档。
```

### 9.8 修 M9（热改会话层原子性）

```text
只做一件事：让热改在会话层也是全有或全无。
对象：LiveSkillWorkbenchRuntime.TryApplyNextCast 与 LiveSkillWorkbenchDataPlane.HandleApply。
现状：Classify 对混合补丁会同时置 canNextCast=true 与 engineRestart=true
（两个独立 ref bool），而 TryApplyNextCast 只判 !CanCommitNextCast 就放行，
不查 RequiresEngineRestart / RequiresMapReload —— 只提交 NextCast 子集、
静默丢弃其余算子，还把状态置成「已应用」。这是 NO FALLBACK 红线：静默丢弃作者意图。
commit 层的回滚（CommitNextCastSafeFrame + CommitRolledBack）是对的，别动。
ApplyClassificationToUiUnlocked 里的 _applySupported=false 只是 UI 标志，不是闸门。
要求：混合分类必须整体拒绝并把原因告诉作者（哪些算子需要重启/重载地图），
不得部分提交。补一条混合会话的测试 —— 今天零覆盖。
参照 LswChampionHotApplyDemoSystem.RunHotApplyFireToIce，它对分类结果逐条校验，
比生产入口更严；生产入口不该比演示宽松。
```

### 9.9 修 M2（无界 materialize）

```text
只做一件事：让 InvokeBuiltin 那间展厅不再无限攒人。
现状：assets/GAS/graphs/InvokeBuiltin.json 的 materialize 节点每次执行都
MaterializeTemplate 一个 GraphOps.Ally，而 GraphOpsNodeGalleryRuntime.Tick 每
0.35 秒重跑一次图，整个画廊 mod 里没有任何 Destroy / Despawn。
这些新身体不在分镜 actors 名单里，所以没名字没血条，但确实活在世界里。
要求：给这间展厅一个明确的生命周期语义并写进分镜数据（例如每拍先回收上一个、
或只在第一拍 materialize 之后展示结果），不要在 C# 里加硬编码的特例分支。
顺带核对：ClearActiveEffects 这个 builtin 在这张图里是否真跑到了 ——
Blackboard 家族有一个只会计数的假 IGraphRuntimeApi，别让画廊也走那条路。
禁止：把 materialize 从图里删掉了事 —— 这间展厅的意义就是演示 InvokeBuiltin。
```

### 9.10 收敛家族场（独立票，不要与上面任何一条合并）

```text
只做一件事：决定八家族 Mod 的去向并执行。
它们现在唯一还在运转的用途是给八个 ci-gate fixture 提供被测对象，而那些 fixture
断言的大多是家族 runtime 自己攒的中文文案和自己维护的计数器，不是引擎行为。
真正有含金量的覆盖只有两处：Spatial 的 AssertTargetListGetOnAllGraphs
（编译产物必须出现 TargetListGet / QueryFilterNotEntity / QueryFilterLayer /
QueryFilterRelationship）与 Script 的 yield/halt 时序。
建议路径：把这两块搬进 per-op 画廊或 Core 图测试，然后删掉八个 Mod 与八个 fixture。
一并带走的债：GraphOpsEngineWorld.AttachOrCreate 的 World.Create() fallback 分支、
空方法 StartOwnedAndTick()、被 `_ = startPath;` 丢弃的参数、
写在生产 mod 里的假 IGraphRuntimeApi（LifecycleShowcaseGraphApi）、
Blackboard 的静默 TryGet 降级与每 wave 泄漏两个实体、
Attr 的私有 EffectRequestQueue 与手工塞 GameplayEffect、
Rel 的「好感当血量画」。
前置依赖：必须先做 9.2（锁门 + 停止清表），否则删除期间玩家仍可点进去。
禁止：在不删除的前提下逐条修补 —— 先拍板去留，再动手。
```

---

## 10. 本报告的范围

- **包含**：`origin/main @ 82ddb3322a` 上图能力收口的玩家门、开图与空间、血条与披露、两本复用清单、作者前门、覆盖表、退役家族残留、L2 与技能热改的符合性判定；与 #914 的衔接；修复提示词。
- **不包含**：实现修复（本轮零生产代码改动）；UI 面板债、表现层改名、#723 评分预算。
- **禁止**：把本文件之外的平行结论当作本轮 SSOT。
