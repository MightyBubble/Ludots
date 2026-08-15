# 审计需求：GAS + Graph 修复计划落地后（#942 全票合入 · 当时需求）

**给审计 / 接手 Agent。**  
这是当时的独立架构审计请求。对象是已经 squash 进 `main` 的 [#942](https://github.com/MightyBubble/Ludots/pull/942) 计划全票。  
结论当时写在 [`s_plan_landed_architecture_audit.md`](s_plan_landed_architecture_audit.md)。**当前进度只认** [图能力唯一入口](../../gitbook/architecture/graph-capability-status.md)。禁止借本需求夹带实现修复，也不要再按本页开一轮新审计。

**事后更正（主干 `d1b8f5f4d7`）：** 八间家族房间已删除（#968），不是「S6 之后另开删房票」。分层 Wave 2–4 脚手架已合（#964），Wave 5–6 仍未做。「残血的分更高」已合（#963）。本页里「尚未开工 / 退役当玩家门」的句子已按现状改。

---

## 1. 概述

计划从「审查里点名的洞」走到「任务书上的票都进了主线」。产品要确认的不是「PR 数字变绿了」，而是：

1. 自己调自己的图不能把游戏弄没了。  
2. 写进属性的数不会自己变回去；结算提交和回滚各走该走的路。  
3. 纯查询不能偷偷跑起会等一拍的动作。  
4. 划掉的展厅门是真锁上的；进一间展厅不会弄坏别的展厅的图编号。  
5. 血条要么代表真被打掉的血，要么别装成那样。  
6. 所有图执行走同一道门；选中看战场坐标，不看镜头裁剪。  
7. 覆盖表说覆盖了就要真跑；验收页只剩一份。  
8. 格子只由分配器发放；行为树和状态机可以用数据写。  
9. 分层墙还在纸上和棘轮上，没有假装已经拆完程序集。  
10. 合同若仍写「修复中」，报告里不得把它当成「已落地」。

| 对象 | 值 |
|------|-----|
| 被审 tip | `origin/main` @ `46fcd9dcda`（#960 合入后） |
| 计划正本 | [`gas_graph_architecture_fix_plan.md`](gas_graph_architecture_fix_plan.md)（[#942](https://github.com/MightyBubble/Ludots/pull/942)） |
| 审查正本 | [`gas_graph_architecture_review.md`](gas_graph_architecture_review.md) |
| 合同 | `gitbook/architecture/graph-funclib-actionlib-contract.md`（状态：**修复中**） |
| 分层 | `gitbook/architecture/graph-layering-flow-and-behavior.md` |
| 属性写入 | `gitbook/architecture/attribute-write-authority.md` |
| 跨层所有权 | `gitbook/architecture/entity-simulation-layering.md` §5.1 |
| 验收页 | `gitbook/acceptance/graph-funclib-actionlib-uat.md` |
| 前序审计（输入，不是结论） | [`s_batch1_architecture_audit.md`](s_batch1_architecture_audit.md)、[`s_batch2_s9_architecture_audit.md`](s_batch2_s9_architecture_audit.md)、[`pr932_graph_landed_architecture_audit.md`](pr932_graph_landed_architecture_audit.md) |
| S14 设计 | [`s14_layering_physicalization_design.md`](s14_layering_physicalization_design.md) |

实现方声称已合入（请逐项证伪，不要把 PR 标题当证据）：

| 任务 | 合入 PR | 实现方声称 | 前序审计怎么说 |
|------|---------|------------|----------------|
| S1 自己调自己会杀进程 | [#944](https://github.com/MightyBubble/Ludots/pull/944) | 登记拒环；漏到运行期抛错，不崩进程 | 第一批：**可关单**。本轮只复验 squash 后是否仍成立 |
| S2 写进去的数会变回去 | [#946](https://github.com/MightyBubble/Ludots/pull/946) | 直写永远存活；展厅也扫；裸写 CI 红 | 第一批当时：**合入，不关单**（只扫 Core）。声称收口已进 #946 |
| S3 查询口偷跑可挂起动作 | [#941](https://github.com/MightyBubble/Ludots/pull/941) | 查询 `InvokeScript` 只许函数名，拒 `graphId` | **从未独立审过** |
| S4 回滚自己会抛 | [#959](https://github.com/MightyBubble/Ludots/pull/959)（#943 已关） | 回滚走完；提交按属性走正式写入面 | 第一批当时：**合入，不关单**。声称收口已进 #959 |
| S5 容量到顶不说话 | [#945](https://github.com/MightyBubble/Ludots/pull/945) | 满了就抛；假计数整套删掉 | 第一批：**可关单**。旁系死计数是已知债 |
| S6 退役门没锁 | [#941](https://github.com/MightyBubble/Ludots/pull/941) 后 [#968](https://github.com/MightyBubble/Ludots/pull/968) 删房 | 当时退役锁门；现在房间已删除 | **已审且事后删房** |
| S7 血条演戏 | [#941](https://github.com/MightyBubble/Ludots/pull/941) | 真结算走图；驱动只回读世界血；茶水不当血 | **从未独立审过** |
| S8 假防线 | [#951](https://github.com/MightyBubble/Ludots/pull/951) | 只打印的补成真断言 | 第二批：**可关单** |
| S9 图执行走同一道门 | [#953](https://github.com/MightyBubble/Ludots/pull/953) | 七宿主走 `GraphExecutor` | 第二批：**可关单**（typed 图号、展厅内部入口是已知缺口） |
| S10 选中读裁剪 | [#952](https://github.com/MightyBubble/Ludots/pull/952) | 点选/Tab 读模拟位置 | 第二批：**可关单** |
| S11 覆盖表假绿 | [#950](https://github.com/MightyBubble/Ludots/pull/950) | 错指针先修；生成器失败关闭 | 第二批：**可关单** |
| S12 格子与前门同一张表 | [#956](https://github.com/MightyBubble/Ludots/pull/956) | 分配器发放格子；前门 ⊆ 策略 | **从未独立审过** |
| S13 数据写行为 | [#960](https://github.com/MightyBubble/Ludots/pull/960) | JSON 树/机；叶子自己读血；HFSM 禁 Yield | **从未独立审过** |
| S14 分层 | [#954](https://github.com/MightyBubble/Ludots/pull/954) 设计 + [#957](https://github.com/MightyBubble/Ludots/pull/957) Wave 1；后续 #964 合入 Wave 2–4 脚手架 | 设计答六问；顺序只认枚举；拿引擎标过时；**不搬家** | 当时设计与 Wave 1 未审；现在 Wave 2–4 已合，Wave 5–6 仍未做 |
| S15 验收页一份 | [#948](https://github.com/MightyBubble/Ludots/pull/948) | 一个 H1；没做的写成没做 | 第二批：**可关单** |

---

## 2. 结构

```text
阶段 0  对齐：读计划、合同、前序审计，禁止重开已裁决争论
阶段 1  三条闸门（从未审）：查询口 / 退役门 / 血条
阶段 2  属性权威收口：第一批当时不让关的 S2 / S4
阶段 3  作者地基（从未审）：S12 格子与前门、S13 数据写行为
阶段 4  分层第一波：设计六问 + Wave 1 棘轮（不审搬家）
阶段 5  前序可关单复验：S1 / S5 / S8 / S9 / S10 / S11 / S15
阶段 6  合成：一份 Verdict，禁止平行结论
```

领域（可并行，阶段 6 必须合成）：

| 领域 | 一句话 | 对应任务 |
|------|--------|----------|
| I 查询口 | 纯查询能不能直绑一张会等一拍的图 | S3 |
| J 退役门 | 划掉的卡还能不能点进去，进去会不会清图号 | S6 |
| K 血条 | 这一刀是真打还是演戏，打到零会不会偷偷回满 | S7 |
| O 属性收口 | 展厅还能不能裸写；提交是不是正式写入面 | S2 / S4 |
| L 格子与前门 | 两个节点会不会抢同一格；文档能写的是否真写得出 | S12 |
| M 数据写行为 | JSON 树是否真在跑；叶子是否还靠 C# 喂数 | S13 |
| N 分层第一波 | 设计有没有答完六问；Wave 1 有没有假装拆完程序集 | S14 |
| P 前序复验 | 已可关单的票在 squash 之后有没有被后票踩坏 | S1 / S5 / S8 / S9 / S10 / S11 / S15 |

交叉审计可用多个子 Agent，**最终只留一份**报告：

`docs/audits/s_plan_landed_architecture_audit.md`

---

## 3. 详情

### 3.1 产品共识（勿再争）

1. Duration / Period 在效果壳上；Effect 图内不用 Yield 冒充时间轴。  
2. FuncLib = 纯（无 Yield）；ActionLib = 可挂起；同名跨库失败关闭。  
3. Effect 可分支 + 调 FuncLib；不得调 ActionLib。查询方言与线性方言同一条红线。  
4. 一种作者边模型 + 一台 VM；禁止平行编译器、平行程序宇宙。  
5. 图节点玩家门是**单节点展厅**；八个家族大杂烩已删除，不是第二套玩家入口。  
6. 人从地图刷；血条走 WorldHud / 生命披露；禁止 C# 改血演戏驱动 HUD。  
7. 选中、点选、下达命令读战场坐标与情报，不读镜头裁剪。  
8. NO FALLBACK：缺清单、空表、未知符号、引擎空、容量满、调用成环，全部失败关闭。  
9. 合同在计划点名的红线收完之前，不得改回「已落地」。  
10. S14 禁止一周全切完；Wave 1 只立墙和棘轮，不搬家。

### 3.2 阶段 × 领域：给审计 Agent 的提示词

下面每块都可以**单独另开一个审计员**。纪律对所有块生效：只读自己工作区；先读 `gitbook/contributing/ai-assisted-development.md` 任务执行决策规范；NO FALLBACK / SSOT / 禁止发明 opcode；证据要有路径；不要重开产品争论；不要把前序审计的「可关单」直接抄成结论。

---

#### 阶段 0 — 所有人先贴（短对齐，不写结论）

```text
你是只读审计员。对象：Ludots origin/main @ 46fcd9dcda（#942 计划全票已合）。
先读并复述（各三句以内，禁止开始改代码）：
1) docs/audits/gas_graph_architecture_fix_plan.md 依赖图与「不在本计划内」
2) gitbook/architecture/graph-funclib-actionlib-contract.md 首页状态与三条边界
3) docs/audits/s_plan_landed_audit_handoff.md 阶段划分：哪些从未审、哪些只复验
刻意不审：#886/#893 UI 面板、#723 GraphScore、#947 Pi。家族房间已删，不要再审「退役锁门」。分层续做只对照设计 Wave 5–6。
完成后停，等阶段任务。
```

---

#### 阶段 1 — 三条闸门（从未审）

**领域 I · S3 查询口**

```text
阶段 1 / 领域 I。只审查询方言能不能直绑可挂起动作。
对象：origin/main @ 46fcd9dcda。
计划任务书：gas_graph_architecture_fix_plan.md §S3。
必读：src/Core/NodeLibraries/GASGraph/GraphControlFlowCompiler.Query.cs、
同目录 GraphControlFlowCompiler.Linear.cs 的 InvokeScript 分支、
src/Tests/GasTests/Graph/GraphEffectAuthoringExpressivenessTests.cs、
gitbook/architecture/graph-funclib-actionlib-contract.md §3.4。
证伪：
1) 查询图 InvokeScript 只给 graphId、不给 functionName，编译是否失败？诊断是否与线性方言同一套，而不是新错误体系？
2) FrontDoor_LinearKindsInvokeScriptGraphId_FailClosed（或等价）是否覆盖 Query 与 Effect？不要只看 Score/Validation/Derived。
3) assets/ 与 mods/ 下是否还有 kind=Query 且带 graphId 的图被放行？
4) 用清单函数名调用是否仍然允许？
5) 有没有用「改合同措辞」或「只拦运行期」代替编译期拒绝？
该跑：--filter "FullyQualifiedName~GraphEffectAuthoringExpressivenessTests|FullyQualifiedName~GraphQueryControlFlowTests|FullyQualifiedName~GraphContractTests"
产出：阻断/Major/Minor，每条给路径。不要写总 Verdict。
```

**领域 J · S6 退役门（事后：房间已删）**

```text
当时审的是退役锁门。现在八间家族房间已按 #968 删除。
不要再按「八条 retired binding」取证。
当前证伪：登记表没有 capability_standard_graph_ops_*；
mods/ 没有 CapabilityStandardGraphOps{Rel,Query,Attr,Spatial,Event,Float,Script,Blackboard}Mod；
启动器没有这些图能力家族旧房间的退役卡片。单节点画廊留下。
```

**领域 K · S7 血条**

```text
阶段 1 / 领域 K。只审玩家看见的血条是不是它声称的东西。
对象：origin/main @ 46fcd9dcda。
计划任务书：gas_graph_architecture_fix_plan.md §S7。
必读：CapabilityStandardGraphOpsNodeGalleryMod/Runtime/Drivers/{Event,Blackboard,Sandbox,Script,Linear}NodeDriver.cs、
同 Mod assets/GAS/graphs/SendEvent.json、FanOutDispatchEffect*.json、LoadConfigEffectId.json、
showcase.registry.json 相关简介、GraphOpsNodeGallery* 测试。
证伪：
1) SendEvent / FanOutDispatch* / LoadConfigEffectId 是否在图里 ModifyAttributeAdd（或等价正式结算），驱动是否只 SyncActorHealthFromWorld？
2) Linear/Event 驱动里是否还留 if (next <= 0) next = opening 这类静默回卷？
3) Script 喝茶是否还把茶水写进 ActorHealth？简介是否还承诺「血条按茶水掉」？
4) 纯算式展厅（AddFloat 等）简介是否还让玩家以为血条是被真打掉的？
5) 知识披露 / 头顶条那一侧有没有被顺手改坏？（计划禁止动那一侧。）
该跑：--filter "FullyQualifiedName~GraphOpsNodeGallery"；python3 scripts/validate-registry.py
产出：把「演戏」和「真结算」分开写。不要写总 Verdict。
```

---

#### 阶段 2 — 属性权威收口

**领域 O · S2 / S4**

```text
阶段 2 / 领域 O。只审第一批当时不让关的两处收口现在是否成立。
对象：origin/main @ 46fcd9dcda。
计划任务书：gas_graph_architecture_fix_plan.md §S2 / §S4。
前序：docs/audits/s_batch1_architecture_audit.md 的 M1 / M2。
必读：gitbook/architecture/attribute-write-authority.md、
src/Tests/ArchitectureTests/Governance/ArchitectureGuardTests.cs
（AttributeBufferWrites_MustComeFromWhitelistedCallers、CollectAttributeBufferWriteScanAssemblies）、
AttributeMutationOps、EffectPhaseSideEffectTransaction、EffectModifierOps、
效果阶段事务的 Commit / Rollback。
证伪：
1) 守卫是否同时扫 Core 和展厅程序集？缺 DLL 是失败关闭还是静默只扫 Core？
2) 展厅（含 GoldMarket 运行时补货）是否还对 AttributeBuffer 裸 SetBase/SetCurrent？
3) EffectPhaseSideEffectTransaction 是否仍在写入白名单？（声称已移出。）
4) 提交路径是否按属性走 AttributeMutationOps.SetBase/SetCurrent，而不是整块赋值缓冲？
5) 回滚是否仍整块恢复事务开始时的缓冲？这是回滚，不是玩法写入——不要把它误判成 S2 违规。
6) 回滚中途缺服务 / 目标被销毁是否仍失败关闭或按任务书选定语义走完，而不是装成零目标？
不要把「聚合过程中当前值短暂被改写」自动升成阻断；第一批已记录为中间态。本轮要看收口项，不是重打第一批主故障。
产出：M1/M2 是否关闭、新债表。不要写总 Verdict。
```

---

#### 阶段 3 — 作者地基（从未审）

**领域 L · S12 格子与前门**

```text
阶段 3 / 领域 L。只审寄存器归属和 kind×opcode 单一数据源。
对象：origin/main @ 46fcd9dcda。
计划任务书：gas_graph_architecture_fix_plan.md §S12。
必读：GraphRegisterFile（或等价分配器）、GraphOpDescriptorTable*、
GraphKindOperationPolicy、GraphProgramAuthoringFrontDoor、
GraphProgramSymbolPatcher、GraphControlFlowConfigCoverageTests。
证伪：
1) TargetListGet 与 SnapToNearestInCollection 的 valid 位是否还写死 B[31]？所有 scratch 是否走 AllocScratch()？
2) PinRegister 与已分配格子冲突时是否编译失败，而不是静默别名？
3) 是否存在一张 descriptor 表，前门矩阵、端口、authorableKinds、策略例外都由它投影？
4) 是否有「前门 ⊆ 策略」的一致性断言？Score/Validation/Derived 的「前门能写、策略拒」是否变成作者能看懂的诊断？
5) GraphProgramSymbolPatcher.Patch 第二次跑会不会把已解析 id 再当符号索引（热改错绑）？
6) 有没有顺手改 32 容量上限？（计划禁止。）
该跑：--filter "FullyQualifiedName~GraphControlFlowConfigCoverageTests|FullyQualifiedName~GraphContractTests|FullyQualifiedName~GraphEffectAuthoringExpressivenessTests|FullyQualifiedName~GraphQueryControlFlowTests|FullyQualifiedName~GraphNodeOpCoverageRegistryTests|FullyQualifiedName~LiveGasEditPipelineTests"
产出：格子冲突表 + 前门/策略缺口表。不要写总 Verdict。
```

**领域 M · S13 数据写行为**

```text
阶段 3 / 领域 M。只审「行为能不能用数据写」。
对象：origin/main @ 46fcd9dcda。
计划任务书：gas_graph_architecture_fix_plan.md §S13。
必读：assets/Configs/AI/behavior_trees.json、hfsm.json 及 schema、
GraphBehaviorDefinitionLoader、GraphBehaviorCatalog、
assets/Configs/GAS/action_lib.json、GraphActionCatalogLoader 的 host/Yield 策略、
ScriptDialectL2AuthoringTests、BehaviorTreeWorld / GraphProgramHfsmHost。
证伪：
1) 巡逻-追击-攻击树是否来自 JSON，而不是 BehaviorTreeFactory.CreatePatrolChaseAttackTree 仍当玩法 SSOT？
2) 叶子读目标血量是否真走 LoadAttribute，不必 C# 先喂 I[0]？默认 bt.seeEnemy / bt.inAttackRange 若仍是 HaltReturnInt+I[0]，记已知缺口，不要自动升阻断，但不得写成「叶子感知已关单」。
3) ActionLib 叶子经帧调 FuncLib（如 demo.const.seven）是否真返回，而不是 Programs 为 null 必抛？
4) 11 个脚本键是否只活在 action_lib.json？Core 里是否还留第二份名字表当玩法 SSOT？
5) HFSM / 关卡 RunScript 挂含 Yield 的动作，是否在加载期失败关闭？合同 §4.4 与实现是否同一裁决（实现方声称：HFSM 禁止 Yield）？
6) 含 Yield 的行为树叶子跨拍是否续跑，而不是每波从根重来？（实现方修过「数据写的巡逻树」Running vs Success。）
7) 手写沙盒图 Dst=255 这类非寄存器口，是否还被当成寄存器越界？（实现方修过 AbilityGraphSandbox_CastArc_UnderBudget。）
该跑：--filter "FullyQualifiedName~Ludots.Tests.Gas.AI|FullyQualifiedName~GraphActionCatalogLoaderTests|FullyQualifiedName~ScriptDialectL2AuthoringTests|FullyQualifiedName~AbilityGraphSandbox_CastArc|FullyQualifiedName~ScriptFlowSandboxShowcaseAcceptanceTests|FullyQualifiedName~GraphBehaviorSeparatedShowcaseAcceptanceTests"
产出：作者面符合性 + 实现方自报缺口核实。不要写总 Verdict。
```

---

#### 阶段 4 — 分层第一波

**领域 N · S14 设计 + Wave 1**

```text
阶段 4 / 领域 N。只审「墙有没有立住」，不审搬家。
对象：origin/main @ 46fcd9dcda。
计划任务书：gas_graph_architecture_fix_plan.md §S14。
必读：docs/audits/s14_layering_physicalization_design.md、
src/Core/Engine/SystemGroupOrder.cs、
src/Core/Engine/Pacemaker/PhaseOrderedCooperativeSimulation.cs、
src/Core/Scripting/ScriptContextExtensions.cs、
src/Tests/ArchitectureTests/Governance/S14LayeringRatchetTests.cs、
ArchitectureGuardTests 里 SystemGroup / PhaseOrder 相关测试。
证伪：
1) 设计是否回答计划里的六个问题（程序集切法、注册表实例化、Mod 可见面、跨层组件、SystemGroup SSOT、分波迁移）？缺一问记 Major。
2) 设计是否仍写「评审通过之前不搬生产代码」？main 上 src/Core/Ludots.Core.csproj 是否仍是单一大程序集？（仍是单一程序集不是缺陷；若已经大搬家，那是超范围，记阻断。）
3) SystemGroupOrder.All 是否就是 Enum.GetValues<SystemGroup>()？PhaseOrderedCooperativeSimulation 是否还有第二份 PhaseOrder 数组？
4) GetEngine 是否标 Obsolete？棘轮数字（声称 GetEngine 205 / 未声明 RegisterSystem 100 / 生产 Registry.Clear 15 / 引用 Facade 136 / mods GraphIdRegistry.Clear 5）是否只降不升？S6 之后 mods Clear 可能已是 0——不要把「低于上限」写成「S14 已关单」。
5) S8 的阶段顺序交叉校验有没有被 Wave 1 删成空断言？设计文档交叉校验（enum 名表）是否还在？
6) 有没有把 gitbook 合同改成「分层已落地」？
该跑：--filter "FullyQualifiedName~S14LayeringRatchetTests|FullyQualifiedName~ArchitectureGuardTests.RuntimeSystemGroupOrder|FullyQualifiedName~ArchitectureGuardTests.PhaseOrdered|FullyQualifiedName~ArchitectureGuardTests.SystemGroup_MustMatch"
产出：设计符合性 + Wave 1 棘轮表。明确写：S14 整体不得关单。不要写总 Verdict。
```

---

#### 阶段 5 — 前序可关单复验

**领域 P · 抽查，不重做整份旧报告**

```text
阶段 5 / 领域 P。只复验第一批/第二批当时可关单的票，在 46fcd9dcda 上有没有被后票踩坏。
对象：origin/main @ 46fcd9dcda。
前序报告：s_batch1_architecture_audit.md、s_batch2_s9_architecture_audit.md。
不要重写那两份报告。只回答「还成立吗」。
抽查清单（每条给现在的路径或测试名）：
S1  自递归 / A→B→A 登记失败；深度超限抛错而不是杀进程
S5  点名队列满了抛；被删的假计数没有回来（事件总线旁系死计数仍是已知债）
S8  以前只打印的基准现在仍有会失败的断言；门槛数字没有被后票放宽
S9  七个生产宿主不直连内部 Execute/ExecuteSlice；typed 图号仍是 int、展厅内部入口仍在——核实存在，不当新发现，也不当已收口
S10 点选/Tab 仍读 WorldPositionCm；镜头外 IsVisible=false 仍可选；守卫仍禁模拟相读 VisualTransform/CullState
S11 覆盖表 covered ⇒ 测试可解析；生成器找不到专属测试仍退出
S15 验收页只有一个 H1；查询 graphId 直绑不得写成合同原文已覆盖
若 squash 后某条主故障回归，升阻断并点名后票。若只是注释/路径漂移，记 Minor。
产出：七行「仍成立 / 回归 / 无法测」表。不要写总 Verdict。
```

---

#### 阶段 6 — 合成（只允许一个 Agent）

```text
阶段 6。你是唯一合成员。收集阶段 1–5 各领域表，只写一份报告：
docs/audits/s_plan_landed_architecture_audit.md
并在 docs/audits/README.md 加目录链接。

报告必须有：
1) Verdict：HOLD MAIN / FIX-FORWARD / REGRESS（禁止用 MERGE，因为已经在 main）
2) 逐票关单表：可关 / 合入不关 / 回归。S14 整体必须显式写「不关单」
3) 阻断 / Major / Minor / 债务表（路径 + 证据）
4) 玩家/作者先写：查询口、退役门、血条、数据写的树，进游戏会看见什么
5) 合同 §3/§4.4/§5/§6 符合性；合同状态该不该继续「修复中」
6) 与第一批 / 第二批 / #932 审计的衔接：哪些已关、哪些仍开、哪些是新债
7) 给后续 Agent 的最短提示词（按票拆：S14 Wave 2+、typed 图号、默认叶子感知、删家族 Mod——不要一条巨提示词）

禁止：多份平行结论；夹带实现；重开产品争论；把 UI 面板/#723/#947/S14 搬家写进阻断。
纪律：NO FALLBACK、SSOT、说人话写场景证据。
```

### 3.3 建议报告骨架

```text
# 1. 概述（Verdict + 玩家一句话 + 逐票关单表）
# 2. 结构（阶段/领域对照）
# 3. 详情（从未审的票先写，复验表后写）
# 4. 场景（玩家看见什么 vs 实际）
# 5. 边界（不审项、已裁决共识）
# 6. UAT（对照下面 §6，标过/未过/无法测）
附录 A 测试证据
附录 B 给后续 Agent 的最短提示词
```

### 3.4 实现方自报、核实即可的缺口

这些是落地时自己写下的，**不要当成新发现**，但也不能写成已经关单：

| 项 | 谁说的 | 怎么核实 |
|----|--------|----------|
| typed 图号仍是 `int` | 第二批 S9 | 生产宿主与 `GraphExecutor` 入口类型 |
| 展厅内部仍走 `Execute` / `ExecuteSlice` | 第二批 S9 | `CapabilityStandardGraphOps*Mod` 与画廊 driver |
| 默认 `bt.seeEnemy` / `bt.inAttackRange` 仍是 `HaltReturnInt` + 可选 `I[0]` | S13 实现方 | `action_lib.json` 与对应图 |
| `BehaviorTreeFactory` 仍留 AlwaysSuccess / HoldRunning / 骨架树 | S13 实现方 | 是否还被玩法树引用，还是只供拓扑/性能 |
| `LevelBlueprintFactory.CreateTwoPhaseTrial` 仍是 C# 关卡骨架 | S13 实现方 | 相位图 id 是否来自 ActionLib |
| Script 查询列表沿用 Effect 隐式 TargetList | S13 实现方 | Query* 是否另开 script `list` 端口 |
| `GraphActionCatalog` 加载后不持久化 `host` | S13 实现方 | 宿主 Yield 策略是否只在装载期跑一次 |
| S5 事件总线旁系死计数恒为 0 | 第一批 | 不要当 S5 回归 |
| S14 Wave 5–6 未做 | Wave 2–4 脚手架已在 #964 | 发现大搬家才是越界 |

---

## 4. 场景

审计时用玩家/作者眼睛，不要用架构名词当场景。

1. 我写了一张只会调用自己的图：登记必须当场失败，游戏还在。  
2. 我给一个不夹上限的属性写了当前值：过几拍再看，数还在。  
3. 我在查询图里填了一个巡逻动作的图号：编译失败，不能靠查询偷偷等一拍。  
4. 启动器里没有家族大杂烩，也没有这些图能力家族旧房间的退役卡片。
5. 我点进「派事件改血」那一类短剧：血条跟结算走；打到零不会无声回满。  
6. 镜头抬高、单位被挡住：我仍然能点到他、能下令。  
7. 我在配置里写了一棵巡逻-追击-攻击树：代理按树走，我不用改引擎 C#。  
8. 叶子要看目标还剩多少血：它自己读，不必先有人把数字塞进来。  
9. 我把含「等一拍」的动作挂到哨兵状态机上：加载就失败，而不是跑到一半炸。  
10. 维护者打开分层设计：能看见六波怎么切；打开工程，Core 仍是一个程序集，没有假装拆完。

---

## 5. 边界

**做**

- 证伪 `main@46fcd9dcda` 上计划全票的声称  
- 从未审过的票（S3 / S6 / S7 / S12 / S13 / S14 设计与 Wave 1 / S2·S4 收口）必须读源码，不能只读 PR 说明  
- 已可关单的票做回归抽查  
- 对照合同与前三份审计，写清仍开的债  
- 一份 SSOT 报告

**不做**

- 改生产代码、顺手修 UI 面板、重开 Duration/Yield 产品争论  
- 把 typed 图号、#723、#947 升成本次唯一目标；家族房间已删，不要再派删房票  
- 用「合同缩编」或改测试门槛掩盖  
- 发明新 graph op / profile enum / 第二套加载器  
- 把第一批/第二批报告复制粘贴改个日期交差

**已知已知（核实，勿当新发现）**

| 项 | 说明 |
|----|------|
| 合同「修复中」 | S13 明确未改落地状态；报告不得写成已落地 |
| 前序审计基线是旧 tip | 第一批/第二批对照的是 `82ddb3322` 上的 PR head；本轮对照 squash 后的 main |
| #943 已关 | 被 #959 取代，不要按 #943 tip 复读 |
| #939 / #955 已关 | 旧审计需求与重复审计，内容分别进了 #941 与 #958 |
| GetEngine 过时警告 | Wave 1 故意标过时，205 处警告不是回归 |
| 八家族房间 | 当时 S6 禁止删除；#968 已删。不要再当「还在」 |

---

## 6. UAT

```gherkin
Feature: 一张图不能把游戏弄没了
  作为技能作者
  我希望自己调自己的图在登记时就被拒绝
  以便游戏还能打开

  Scenario: 自己调自己必须被拒绝
    Given 我写了一张脚本图，它唯一做的事就是调用自己
    When 这张图被登记进图注册表
    Then 登记必须失败并告诉我这里有一个调用环
    And 游戏不应该能带着这张图启动

Feature: 写进去的数还在
  作为数值作者
  我希望正式接口写进去的当前值过几拍还在
  以便我不靠「夹不夹上限」碰运气

  Scenario: 不夹上限的属性也能保住写入
    Given 我通过正式写入面给一个不夹上限的属性写下当前值
    When 属性重算跑完
    Then 我写下的数还在
    And 展厅里的裸写如果还在，守卫必须红

Feature: 纯查询里不能偷偷跑起会等一拍的动作
  作为技能作者
  我希望查询图不能直绑巡逻动作的图号
  以便结算不会做到一半停住

  Scenario: 查询图直绑图号必须被拒绝
    Given 我在一张查询图里写了一个调用，直接指向某个巡逻动作的图号
    When 这张图通过作者前门编译
    Then 编译必须失败，理由与线性方言一致

Feature: 家族大杂烩不能再当玩家入口
  作为玩家
  我希望按家族打包的房间不存在
  以便我不会走进一间会弄坏别的展厅的房间

  Scenario: 启动器里没有这些房间
    Given 每个图节点都有自己的短剧
    When 我打开启动器
    Then 我看不到属性族、查询族这类大杂烩入口
    And 登记表里也没有这些房间的退役卡片

Feature: 血条要么代表真被打掉的血，要么别装成那样
  作为新玩家
  我希望点进「派事件改血」时看见的掉血是真的
  以便我看懂这一刀

  Scenario: 打到零不许偷偷回满
    Given 一间展厅里这一刀足以把木桩的血打到零
    When 这一刀结算
    Then 结果必须由数据决定，而不是被代码悄悄改回满血

  Scenario: 喝茶不是掉血
    Given 脚本短剧里角色在喝茶
    When 我看血条和简介
    Then 茶水不得被写成生命值
    And 简介不得让我以为血条是被茶水打掉的

Feature: 被挡住的人还能点到
  作为玩家
  我希望镜头抬高之后仍能点到山坡后的人
  以便我不是在跟摄像机下棋

  Scenario: 看不见也能下令
    Given 场上有一个单位被镜头裁掉
    When 我点他并下达命令
    Then 命令应当落到这个单位上
    And 游戏不得因为他现在画不出来就当他不存在

Feature: 行为可以用数据写，不必改引擎
  作为玩法作者
  我希望在配置里写巡逻树
  以便换一棵树不用改 Core

  Scenario: Mod 定义自己的行为树
    Given 我在配置里写了一棵巡逻-追击-攻击的树
    When 游戏加载这些数据
    Then 代理应当按我写的树行动
    And 我不需要改 Core 的任何玩法 C# 工厂

  Scenario: 叶子自己能感知
    Given 一个行为树叶子需要读取目标的血量来决定是否撤退
    When 它执行
    Then 它应当能直接读到血量
    And 不需要 C# 侧先把结果喂进来

  Scenario: 哨兵机不能挂会等一拍的动作
    Given 我把一个含「等一拍」的动作挂到状态机的心跳上
    When 游戏加载这份配置
    Then 加载必须失败并说明这个宿主不许挂起

Feature: 层与层之间的墙还没假装砌完
  作为维护者
  我希望分层设计能回答怎么切，并且工程还没被一周拆烂
  以便后面可以一波一波搬

  Scenario: 设计能回答边界问题
    Given 一份分层物理化设计已经在仓库里
    When 我按计划里的六个问题核对
    Then 每个问题都有书面答案
    And 工程里的 Core 仍是一个程序集
    And 阶段顺序只认枚举声明，不再另藏一张表
```

---

## 7. 必读（最短）

- 本文件  
- `docs/audits/gas_graph_architecture_fix_plan.md`  
- `docs/audits/gas_graph_architecture_review.md`  
- `docs/audits/s_batch1_architecture_audit.md`  
- `docs/audits/s_batch2_s9_architecture_audit.md`  
- `docs/audits/s14_layering_physicalization_design.md`  
- `gitbook/architecture/graph-funclib-actionlib-contract.md`  
- `gitbook/architecture/attribute-write-authority.md`  
- `gitbook/acceptance/graph-funclib-actionlib-uat.md`  
- `gitbook/contributing/ai-assisted-development.md`（任务执行决策规范）

---

## 8. 本需求文档的范围

- **包含**：上下文、阶段/领域提示词、产出格式、UAT。  
- **不包含**：审计结论、实现修复。  
- **禁止**：多份平行报告；把提示词合成一条让一个人同时审所有领域。
