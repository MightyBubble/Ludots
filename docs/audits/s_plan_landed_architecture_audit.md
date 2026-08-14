# GAS + Graph 修复计划落地后架构审计（#942 全票合入 · 当时结论）

**当时对象：** `origin/main` @ `46fcd9dcda`（#960 合入后）  
**现在怎样：** [图能力唯一入口](../../gitbook/architecture/graph-capability-status.md)  
**需求正本：** [`s_plan_landed_audit_handoff.md`](s_plan_landed_audit_handoff.md)  
**计划正本：** [`gas_graph_architecture_fix_plan.md`](gas_graph_architecture_fix_plan.md)（[#942](https://github.com/MightyBubble/Ludots/pull/942)）  
**合同：** `gitbook/architecture/graph-funclib-actionlib-contract.md`（状态仍是**修复中**）  
**前序（输入，不是结论）：** [`s_batch1_architecture_audit.md`](s_batch1_architecture_audit.md)、[`s_batch2_s9_architecture_audit.md`](s_batch2_s9_architecture_audit.md)、[`pr932_graph_landed_architecture_audit.md`](pr932_graph_landed_architecture_audit.md)  
**方法：** 对照源码与指定过滤器证伪；零生产代码。  
**刻意不审（当时）：** UI 面板（#886 / #893）、#723 GraphScore、#947 Pi。

**事后更正（主干 `d1b8f5f4d7`）：** 本页写于 #960 刚合时。之后 #965 收了夹具、验收页、示意条；#964 合了分层脚手架（墙仍未立完）；#968 **删掉**八间家族房间，不是退役锁门；#963 残血打分短剧已合。S6 / 场景 4 / 「八家族 Mod 还在」已按现状改。其余取证仍是当时 tip 的证据。和现状页打架，以现状页为准。

---

## 1. 概述

### 1.1 Verdict

**FIX-FORWARD。**

玩家能感觉到的主故障，在当时那棵树上已经关上门：自己调自己不会把游戏弄没；写进去的数不会自己变回去；查询图不能偷偷绑一张会等一拍的动作；「派事件改血」打到零不会无声回满；被镜头挡住的人还能点到。作者也能用配置写巡逻树，哨兵机挂「等一拍」会在加载时失败。家族大杂烩当时是退役锁门；**现在房间已删除**，启动器里没有退役卡片。

不能写成「计划已落地、合同改回已落地」。三件事还开着：

1. **分层墙当时还在纸上和棘轮上。** 后来 #964 合了脚手架，墙仍没砌完。S14 整体不得关单。
2. **后票踩坏了一批夹具。** 当时点名过滤器有红。#965 已把夹具、验收页、示意条收干净。不要把当时的红当成现在的红。
3. **验收页当时过时。** #965 已改。合同必须继续写「修复中」。

没有新的阻断项。没有「一行配置杀死进程」或「退役门又能点进去」这种 P0 回潮。

### 1.2 玩家一句话

进游戏：没有按家族打包的大杂烩，也没有退役卡片；这一刀该掉的血会掉、掉到零不会偷偷回满；山坡后的人仍能点到。写技能：自己调自己当场被拒；查询图不能绑巡逻动作。写行为：巡逻树可以写在配置里，但默认「看见敌人 / 进入射程」这两片叶子还要 C# 喂一个数字。维护者：分层怎么切已经写清，工程没有假装拆完。

### 1.3 逐票关单表

| 任务 | 合入 | 本轮 | 关单 |
|------|------|------|------|
| S1 自己调自己会杀进程 | #944 | 复验：登记拒环，运行期超深抛错 | **可关** |
| S2 写进去的数会变回去 | #946 | 第一批 M1 已收口：守卫扫 Core+展厅，缺 DLL 失败关闭；已扫程序集无裸写 | **可关**（扫描名单手工枚举，记债） |
| S3 查询口偷跑可挂起动作 | #941 | **首次独立审。** 查询 `InvokeScript.graphId` 编译失败，诊断与线性方言同一套 | **可关** |
| S4 回滚自己会抛 / 提交对齐写入面 | #959 | 第一批 M2 已收口：提交逐属性走正式写入面；事务类已移出白名单。回滚整块恢复快照是回滚语义，不是违规 | **可关**（点名回滚图用例被 S9 踩红，记 Major） |
| S5 容量到顶不说话 | #945 | 复验：满了就抛；假计数未回 | **可关**（事件总线旁系死计数仍是已知债） |
| S6 退役门没锁 | #941 后由 #968 删房 | **当时**八条退役锁门。**现在**八间家族房间已删除，登记表没有退役卡片 | **可关** |
| S7 血条演戏 | #941 | **首次独立审。** 静默回卷已删；喝茶不写生命；「派事件改血」走图内正式加减 | **可关**（Clamp/Mul/Sub 简介仍像真结算，记 Major） |
| S8 假防线 | #951 | 复验：断言还在、门槛没放宽。点名过滤器 8 红 / 26 绿，是后票踩夹具，不是假防线回来 | **可关**（夹具对齐另开，不要重开 S8） |
| S9 图执行走同一道门 | #953 | 复验：七个生产宿主走 `GraphExecutor` | **可关**（typed 图号、展厅内部入口仍是已知缺口） |
| S10 选中读裁剪 | #952 | 复验：点选/Tab 读战场坐标；镜头外仍可选；守卫仍禁模拟相读裁剪 | **可关** |
| S11 覆盖表假绿 | #950 | 复验：`covered` 可解析；生成器 `--strict` 零漂移 | **可关** |
| S12 格子与前门同一张表 | #956 | **首次独立审。** 临时格走分配器；前门 ⊆ 策略为空集；32 上限未改 | **可关**（双钉同格、`PatchFuncLib` 非幂等记债） |
| S13 数据写行为 | #960 | **首次独立审。** 巡逻树来自 JSON；HFSM/关卡挂 Yield 加载失败；叶子自读能力已开，默认发货图未换 | **可关**（默认感知叶不得写成已关） |
| S14 分层 | #954 设计 + #957 Wave 1；后有 #964 脚手架 | **当时**没有拆程序集。**现在**有两份薄契约，Core 仍是大程序集，墙未立完 | **不关单** |
| S15 验收页一份 | #948 | 复验：仍一个 H1。查询口段落与已落地代码矛盾 | **可关**（页要改写成「已关」，不要再写「未合入」） |

---

## 2. 结构

```text
阶段 0  对齐计划 / 合同 / 前序审计
阶段 1  三条闸门（从未审）：S3 查询口 / S6 退役门 / S7 血条
阶段 2  属性权威收口：S2 / S4（第一批当时不让关）
阶段 3  作者地基（从未审）：S12 格子与前门 / S13 数据写行为
阶段 4  分层第一波：S14 设计六问 + Wave 1 棘轮（不审搬家）
阶段 5  前序可关单复验：S1 / S5 / S8 / S9 / S10 / S11 / S15
阶段 6  本页合成
```

| 领域 | 一句话 | 对应 |
|------|--------|------|
| I | 纯查询能不能直绑一张会等一拍的图 | S3 |
| J | 划掉的卡还能不能点进去，会不会清图号 | S6 |
| K | 这一刀是真打还是演戏，打到零会不会偷偷回满 | S7 |
| O | 展厅还能不能裸写；提交是不是正式写入面 | S2 / S4 |
| L | 两个节点会不会抢同一格；文档能写的是否真写得出 | S12 |
| M | JSON 树是否真在跑；叶子是否还靠 C# 喂数 | S13 |
| N | 设计有没有答完六问；有没有假装拆完程序集 | S14 |
| P | 已可关单的票在 squash 之后有没有被后票踩坏 | S1 / S5 / S8 / S9 / S10 / S11 / S15 |

---

## 3. 详情

### 3.1 从未审的票

#### S3 · 查询口 — 可关

查询图的 `InvokeScript` 只给图号、不给函数名，编译失败。诊断码与线性方言同一套：缺函数名是 `MissingNodeRef`，带了 `graphId` 是 `TypeMismatch`，文案都是「linear FuncLib authoring」。`FrontDoor_LinearKindsInvokeScriptGraphId_FailClosed` 已带 `Query` 与 `Effect`。`assets/` / `mods/` 里没有 `kind=Query` 且带 `graphId` 的图。用清单函数名调用仍然允许。

没有改合同措辞来代替编译期拒绝。合同 §3.4 仍只写 Effect 禁 `graphId`，Query 收口在代码里做了、合同投影不完整——记 Minor，不挡关单。

运行期 `HandleInvokeScript` 仍只验「是 Script、不含 Yield」，不验是不是 FuncLib。作者 JSON 路径已关；绕过前门的 bytecode 仍能登记。这是纵深，不是今天作者能写进查询图的口。

#### S6 · 退役门 — 可关（事后：房间已删）

**当时（`46fcd9dcda`）：** 八条 `capability_standard_graph_ops_*` 是 `binding: null`、`preset: null`、`status: retired`。启动器不给命令。生产 bootstrap 不再清图号。八个家族 Mod 目录还在，勾选列表还能手动打开。

**现在（`d1b8f5f4d7`）：** #968 把这八间房间和对应 Mod **删掉了**。登记表没有这八条，也没有退役卡片。玩家入口只剩一节点一间短剧。不要再写「划掉的卡进不去」。

#### S7 · 血条 — 可关

「派事件改血 / 扇出 / 读配置效果号」三张图里都有 `ModifyAttributeAdd`（−18），驱动只从世界把血读回来。全画廊搜不到 `if (next <= 0) next = opening`。喝茶只写字幕里的水位，不写生命；简介已改成「水位示意，不是血量」。知识披露 / 头顶条那一侧没有被顺手改坏。

演戏和真结算要分开看：

| 玩家点进去 | 条上的数从哪来 | 算不算这一刀 |
|------------|----------------|--------------|
| 派事件改血、扇出、读配置效果号 | 图里正式加减属性，驱动只回读 | 是（扇出模板本身不承担伤害，扣在显式目标上） |
| 加减乘钳一类算式 | 驱动把算式结果画到条上 | 否。`AddFloat` 已写明「示意，不是结算」；`ClampFloat` / `MulFloat` / `SubFloat` 仍写「血条按…掉」 |
| 喝茶 | 条不动，字幕涨水位 | 否。文案说条示水位，条实际不跟茶水走 |

静默回卷这个主故障已经不在。文案批次不一致是 Major，不是阻断。

#### S12 · 格子与前门 — 可关

`TargetListGet` 与 `SnapToNearestInCollection` 的有效位不再写死 B[31]，都走 `AllocScratch()`。先分配再钉格会报冲突。存在 `GraphOpDescriptorTable`；「前门 ⊆ 策略」测试在本 SHA 上空集（六种 kind 都是 0）。`Patch()` 第二次是空操作。32 格上限未改。

还没收干净的：先钉后分配只是跳过已占格、不报冲突；双钉同一格没有诊断；`PatchFuncLib` 没有幂等守卫；编译 emit / 符号补丁仍是大 switch，没有完全由 descriptor 驱动。这些不够把「两个节点抢同一格」的主故障重开。

#### S13 · 数据写行为 — 可关，感知叶不关

巡逻-追击-攻击树来自 `assets/Configs/AI/behavior_trees.json`。Core 里已经没有 `CreatePatrolChaseAttackTree`。11 个脚本名只活在 `action_lib.json`。行为树叶子经帧调用 FuncLib（`demo.const.seven`）能返回 7。含 Yield 的巡逻叶跨拍续跑。HFSM / 关卡挂含 Yield 的动作在加载期失败关闭，与合同「HFSM 禁止 Yield」同一裁决。

默认 `bt.seeEnemy` / `bt.inAttackRange` 仍是 `HaltReturnInt` + `I[0]`。展厅 C# 仍往 `ints[0]` 里喂「看见 / 进入射程」。引擎已经允许叶子自己 `LoadAttribute`，专项测试也证明不必先喂数字——**发货内容还没换**。实现方自报的这条缺口核实成立，不得写成「叶子感知已关单」。

#### S14 · 分层 — 不关单

设计回答了计划里的六个问题：程序集切法、注册表实例化、Mod 可见面、跨层组件、SystemGroup 单一数据源、分波迁移。设计仍写「评审通过之前不搬生产代码」。`Ludots.Core.csproj` 仍是单一大程序集——这是本波预期。gitbook 没有把分层改成「已落地」。

Wave 1：`SystemGroupOrder.All` 就是枚举声明顺序；合作仿真不再另藏一张阶段表；`GetEngine` 已标过时；棘轮只许下降。S8 的阶段顺序交叉校验没有被删成空断言，换成了「运行时表 = 枚举」加「禁止第二张表」。

棘轮实测：`GetEngine` 205 / 未声明 `RegisterSystem` 100 / 引用门面 136 三项顶格；生产静态 `Clear` 10（上限 15）、mods `GraphIdRegistry.Clear` 0（上限 5）。低于上限来自 S6，不是 Wave 1 主动收口，**不得写成 S14 已关**。Wave 2–4 脚手架后来在 #964；Wave 5–6 仍未做。

### 3.2 第一批当时不让关的收口

| 第一批 | @ `46fcd9dcda` | 本轮 |
|--------|----------------|------|
| M1 守卫只扫 Core；展厅裸写；文档缩到 Core | 守卫硬编码 11 个程序集（1 Core + 10 展厅/基准）；缺 DLL `Assert.Fail`；GoldMarket 补金走 `AttributeMutationOps.SetCurrent`；文档写 Core+展厅 | **关闭** |
| M2 提交整块赋值缓冲；事务类在白名单 | `Commit` 按属性走 `AttributeMutationOps.SetBase` / `SetCurrent`；事务类已移出白名单；回滚仍整块恢复快照 | **关闭** |

新债：扫描名单漏了画廊与行为公共程序集（当前源码合规，回归裸写不会被扫到）；`PrepareCommitState` 与正式写入面对脏标记仍有双轨；缺「多目标扇出中途销毁再回滚」的集成测试。

### 3.3 前序可关单复验

| 项 | 还成立吗 | 现在的路径 / 测试 |
|----|----------|-------------------|
| S1 | **仍成立** | `GraphProgramRegistry.EnsureNoInvokeCycle`；`GraphInvokeCycleTests` 在本轮 100 条绿集里 |
| S5 | **仍成立** | `DeferredTriggerQueue` / `EffectRequestQueue` 满了抛；假计数未回。`GameplayEventBus.DroppedEventsLastUpdate` 仍恒 0，不是回归 |
| S8 | **关单条件仍成立；过滤器红** | 门槛字面量未放宽（`<=64` / `<15ms` / `<5ms` / `<50ms`）。8 条红是 S9 要显式结束、S5 容量到顶、S2 当前值不再被聚合改写——夹具没改 |
| S9 | **仍成立** | 七宿主走 `GraphExecutor`。typed 图号仍是 `int`；展厅内部仍走 `Execute` / `ExecuteSlice`。核实存在，不当新发现，也不当已收口 |
| S10 | **仍成立** | 点选/Tab 读 `WorldPositionCm`；`CameraCulledEntity_RemainsSelectableAndReceivesOrders` 绿；守卫 51/51 |
| S11 | **仍成立** | 覆盖表 120 条 `covered`；生成器 `--strict` 后覆盖表与登记表零漂移 |
| S15 | **一份 H1 仍成立；正文过时** | 仍一个一级标题。第 81 行仍写查询口未合、过滤器不含 Query——与本树代码相反 |

S8 不得写成「假防线回归」。断言有牙齿，所以夹具合同一变就红。另开夹具对齐票，不要为了变绿放宽 64，也不要重开 S8。

### 3.4 阻断 / Major / Minor / 债务

**阻断：** 无。

| # | 级 | 项 | 证据 |
|---|----|----|------|
| M-A | Major | S8 点名过滤器 squash 后 8 红 | `DurationEffectTick` 期望 107 实得 100（S2 恢复当前值）；6 条 `MissingHalt` / `PcOutOfRange`（S9）；`Benchmark_DeferredTriggerQueue_Enqueue` 一次塞 10000 条，容量 1024+1024（S5） |
| M-B | Major | S4 点名回滚图用例登记失败 | `InstantGraph_WhenLaterOperationFails_RollsBackAllStagedSideEffects` → `MissingHalt`。实现仍在，夹具手写图没有 `HaltReturnInt` |
| M-C | Major | 验收页仍写查询口未关 | `gitbook/acceptance/graph-funclib-actionlib-uat.md` 第 81 行 vs `GraphControlFlowCompiler.Query.cs` 与已带 Query 的过滤器 |
| M-D | Major | Clamp / Mul / Sub 简介仍像真结算 | `showcase.registry.json`：`血条按钳住后的数掉` / `按放大后的数往下掉` / `按剩下的数掉`；实现与已披露的 `AddFloat` 一样是示意条 |
| M-E | Major（当时） | 退役 `binding=null` 没有书面规则 | #965 已补书面规则。#968 之后房间已删，这条不再是现状 |
| m1 | Minor | 守卫扫描名单手工枚举，漏画廊与行为公共程序集 | `CollectAttributeBufferWriteScanAssemblies` |
| m2 | Minor | 合同未显式写 Query 禁 `graphId` | `graph-funclib-actionlib-contract.md` §3.4 |
| m3 | Minor | 双钉同格、`PatchFuncLib` 非幂等 | `GraphRegisterFile.Pin`；`GraphProgramSymbolPatcher.PatchFuncLib` |
| m4 | Minor | 测试仍手写一份 `DesignedSystemGroupOrder` | `ArchitectureGuardTests.cs`；运行时 SSOT 已是枚举 |
| m5 | Minor（当时） | Rel/Query 家族 Mod 手动勾选后半残 | #968 已删这些 Mod，不再是现状 |
| d1 | 债 | 默认 `bt.seeEnemy` / `bt.inAttackRange` 仍吃 `I[0]` | 实现方自报，核实。不得当新发现，也不得当已关 |
| d2 | 债 | typed 图号仍是 `int`；展厅内部入口仍在 | 第二批已知缺口 |
| d3 | 债 | `GameplayEventBus` 旁系死计数恒 0 | 第一批已知债 |
| d4 | 债 | S14 Wave 5–6 未做 | Wave 2–4 脚手架已在 #964。墙没砌完。发现大搬家才是越界 |
| d5 | 债 | `BehaviorTreeFactory` 骨架树、`LevelBlueprintFactory.CreateTwoPhaseTrial` 仍是 C# | 实现方自报，核实：不是巡逻树 SSOT |

---

## 4. 场景

1. **自己调自己。** 登记失败，游戏还在。复验仍成立。
2. **不夹上限的属性写下当前值。** 重算之后数还在。S2 自己的 21 条测试全绿。
3. **查询图填巡逻动作的图号。** 编译失败，理由与线性方言一致。作者今天写不进去。
4. **启动器里没有家族大杂烩。** 当时是退役锁门；现在房间已删除，也没有退役卡片。
5. **点进「派事件改血」。** 木桩从 100 到 82，数从图里来。打到零不会被代码改回满血。加减乘钳那几间仍是示意条，其中三间简介还没说清楚。
6. **镜头抬高、单位被挡住。** 仍能点到、能下令。
7. **配置里写巡逻-追击-攻击树。** 代理按树走，不用改 Core 工厂。
8. **叶子要看目标还剩多少血。** 引擎允许它自己读。默认那两片「看见 / 进入射程」还要有人先把 0/1 塞进来。
9. **把含「等一拍」的动作挂到哨兵机上。** 加载就失败。
10. **打开分层设计。** 六问都有答案。打开工程，Core 仍是一个程序集，没有假装拆完。

---

## 5. 边界

**做了：** 证伪 `main@46fcd9dcda` 上计划全票的声称；从未审的票读源码；已可关单的票做回归抽查；对照合同与前三份审计写清仍开的债。

**没做：** 改生产代码；审 UI 面板 / #723 / #947；审 S14 搬家；把 typed 图号或删家族 Mod 升成本次唯一目标；把合同改成「已落地」。

**已裁决、不再争：** Duration/Period 在效果壳上；FuncLib 纯、ActionLib 可挂起；图节点玩家门是单节点展厅；选中读战场坐标；缺清单/空表/成环失败关闭；S14 禁止一周拆完。

**已知已知（核实，不当新发现）：** 合同「修复中」；第一批/第二批对照的是旧 tip 上的 PR head；#943 已被 #959 取代；GetEngine 过时警告是 Wave 1 故意的。八家族房间后来按 #968 删除，不再是「S6 要求留着」。

---

## 6. UAT

对照需求 §6。过 / 未过 / 无法测：

```gherkin
Feature: 一张图不能把游戏弄没了
  Scenario: 自己调自己必须被拒绝
    状态: 过
    证据: GraphInvokeCycleTests；登记拒环，运行期超深抛错

Feature: 写进去的数还在
  Scenario: 不夹上限的属性也能保住写入
    状态: 过
    证据: AttributeAggregatorTests 21/21，含 UnconstrainedAttribute_DirectCurrentWriteSurvivesActiveAggregation
    注: 展厅已扫程序集无裸写；名单外程序集是债，不是本场景未过

Feature: 纯查询里不能偷偷跑起会等一拍的动作
  Scenario: 查询图直绑图号必须被拒绝
    状态: 过
    证据: FrontDoor_LinearKindsInvokeScriptGraphId_FailClosed("Query")；Query.cs 与 Linear.cs 同一套诊断

Feature: 家族大杂烩不能再当玩家入口
  Scenario: 启动器里没有这些房间
    状态: 过（事后更正）
    证据: #968 删除八间房间与 Mod；登记表没有退役卡片
    注: 当时证据是退役锁门；现在是房间不存在

Feature: 血条要么代表真被打掉的血，要么别装成那样
  Scenario: 打到零不许偷偷回满
    状态: 过
    证据: 画廊无 next<=0 回卷；SendEvent 100→82
  Scenario: 喝茶不是掉血
    状态: 过（条不跟茶水走，简介已不再承诺按茶水掉）
    注: 文案说条示水位，条实际钉在开局，玩家若只看条会误解

Feature: 被挡住的人还能点到
  Scenario: 看不见也能下令
    状态: 过
    证据: CommandSourceAcquisitionSystem_CameraCulledEntity_RemainsSelectableAndReceivesOrders

Feature: 行为可以用数据写，不必改引擎
  Scenario: Mod 定义自己的行为树
    状态: 过
    证据: behavior_trees.json 的 bt.patrolChaseAttack；Core 无 CreatePatrolChaseAttackTree
  Scenario: 叶子自己能感知
    状态: 未过（能力过、发货未过）
    证据: ScriptDialectL2AuthoringTests 证明 LoadAttribute 可行；默认 bt.seeEnemy / bt.inAttackRange 仍 HaltReturnInt+I[0]
  Scenario: 哨兵机不能挂会等一拍的动作
    状态: 过
    证据: GraphActionCatalogLoaderTests 含 Yield 的 HFSM/关卡动作加载期失败关闭

Feature: 层与层之间的墙还没假装砌完
  Scenario: 设计能回答边界问题
    状态: 过（设计 + Wave 1 墙；整票不关单）
    证据: s14_layering_physicalization_design.md 六问；Ludots.Core.csproj 仍单一程序集；SystemGroupOrder.All = 枚举
```

合同 §3 / §4.4 / §5 / §6：查询禁直绑图号在实现里已立，合同正文没写全；HFSM 禁 Yield 合同与实现同一裁决；分层未落地。**合同状态必须继续「修复中」。**

---

## 附录 A — 测试证据

对象：`/tmp/s-audit/main46` @ `46fcd9dcda`。独立工作区，不改生产。

| 过滤器 / 命令 | 结果 |
|---------------|------|
| ArchitectureTests：`AttributeWriteAuthorityGuardTests` + S14 棘轮 + SystemGroup/PhaseOrder + 选中守卫 | 11 / 11 |
| ArchitectureTests：`ArchitectureGuardTests` 全类 | 51 / 51 |
| GasTests：作者前门 / 查询 / 合同 / 自递归 / 同一道门 / 覆盖表 / 寄存器 | 100 / 100 |
| GasTests：AI + ActionLib + Script 方言 + 沙盒 / 行为展厅 | 40 / 40 |
| GasTests：画廊 + Rel/Query/Attr/Spatial/Event 家族验收 | 112 / 112 |
| GasTests：`AttributeAggregatorTests` | 21 / 21 |
| GasTests：`LiveGasEditPipelineTests` | 14 / 14 |
| GasTests：点选 / 命令源 / 指令缓冲 | 55 / 55 |
| GasTests：`InstantEffectTransactionTests` | **14 / 16**（2 红：`MissingHalt`） |
| GasTests：Allocation / GraphPerf / GasBenchmark / EffectPhaseStress / GraphFailFast | **26 / 34**（8 红，见 §3.4 M-A） |
| `python3 scripts/validate-registry.py` | 错误 0，警告 23（T1 缺截图） |
| `python3 scripts/generate-graph-op-node-galleries.py --strict` 后覆盖表/登记表 | 零漂移 |

完整失败原文见产物 `s_plan_landed_audit_test_evidence.log`。

---

## 附录 B — 给后续 Agent 的最短提示词

按票拆。不要合成一条巨提示词。不要改合同落地状态。不要重开 Duration/Yield 产品争论。

### B.1 夹具对齐（S9 Halt + S5 容量 + S2 当前值）— 不要重开 S8 / S4

```text
对照 docs/audits/s_plan_landed_architecture_audit.md M-A / M-B。
对象：手写 GraphInstruction[] 的测试，以及 DeferredTrigger 基准。
要做：每张手写图末尾加 HaltReturnInt；DurationEffectTick 改断言 current 仍为写入值、cap 为 107，不要把聚合器改回覆盖 current；DeferredTrigger 基准按容量塞满再 Clear，不要为了变绿把容量调大，也不要放宽 64 字节门槛。
禁止：重开 S8；改 GraphVmLimits 32；改 S2 直写存活语义。
该跑：InstantEffectTransactionTests；AllocationTests.DurationEffectTick*；GraphPerfTests.Benchmark_GraphExecutor_SmallProgram；GasBenchmarkTests.Benchmark_DeferredTriggerQueue_Enqueue；EffectPhaseStressTests 里三条零分配；GraphFailFastAndCapacityTests.RelationshipQuery_AllowTruncated*
```

### B.2 验收页按实改写（S15 正文，不是重开 S15）

```text
对照 gitbook/acceptance/graph-funclib-actionlib-uat.md 第 81 行。
查询方言 InvokeScript.graphId 已在 GraphControlFlowCompiler.Query.cs 拒绝；
FrontDoor_LinearKindsInvokeScriptGraphId_FailClosed 已含 Query。
把「本树未合入查询口收口」改成已覆盖，并指向上述测试。
不要把 InvokeAction 原文场景写成已覆盖（全仓仍无该节点）。
保持一个 H1。不要改合同落地状态。
该跑：pwsh ./scripts/validate-docs.ps1
```

### B.3 默认感知叶换发货图（S13 已知缺口）

```text
对照 docs/audits/s_plan_landed_architecture_audit.md d1。
bt.seeEnemy / bt.inAttackRange 仍是 HaltReturnInt + I[0]。
把这两张图改成自己 LoadAttribute / Query，去掉展厅 WriteSensors 对这两 id 的喂数。
BehaviorTreeFactory 骨架树可以留着给压测。
禁止：把 I[0] 旁路写成「已关单」却不改资产；发明新 opcode。
该跑：ScriptDialectL2AuthoringTests；BehaviorTreeRuntimeTests；GraphBehaviorSeparatedShowcaseAcceptanceTests
```

### B.4 S14 Wave 2（只做这一波）

```text
对照 docs/audits/s14_layering_physicalization_design.md §3.6 Wave 2。
只做 ModRegistrySet 实例化设计落地的那一波：注册表从进程静态变成实例状态。
禁止：拆 csproj；删 GetEngine；一周搬完；把 gitbook 改成分层已落地。
棘轮常量只许下降。mods GraphIdRegistry.Clear 已是 0，把上限从 5 降到 0。
该跑：S14LayeringRatchetTests；ArchitectureGuardTests.RuntimeSystemGroupOrder*
```

### B.5 typed 图号（S9 已知缺口，独立票）

```text
对照第二批审计与本报告 d2。
生产宿主与 GraphExecutor 入口的图号仍是 int。
本票只做类型化图号，不要顺手改展厅 InternalsVisibleTo。
禁止：放宽 kind；拓宽 Script。
```

### B.6 删八个家族 Mod — 已做完（#968）

```text
已删 CapabilityStandardGraphOps{Rel,Query,Attr,Spatial,Event,Float,Script,Blackboard}Mod。
登记表没有这八条，也没有退役卡片。不要再派「先锁门再删」的票。
单节点画廊留下。
```

### B.7 简介对齐示意条（S7 文案）

```text
对照 showcase.registry.json 的 ClampFloat / MulFloat / SubFloat。
按 AddFloat 的句式写明「是把算式结果画上去的示意，不是结算出来的伤」。
不要改 LinearNodeDriver 的示意路径，那是算式展厅的既定语义。
该跑：python3 scripts/validate-registry.py
```
