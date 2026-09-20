# S 第一批架构审计：#944 / #946 / #943 / #945

**当时对象：** GAS + Graph VM 修复计划（[#942](https://github.com/MightyBubble/Ludots/pull/942) / [`gas_graph_architecture_fix_plan.md`](gas_graph_architecture_fix_plan.md)）的第一批四张票  
**现在怎样：** [图能力唯一入口](../../gitbook/architecture/graph-capability-status.md)  
**当时对照：** [`gas_graph_architecture_fix_plan.md`](gas_graph_architecture_fix_plan.md) §S1 / §S2 / §S4 / §S5；审查正本 [`gas_graph_architecture_review.md`](gas_graph_architecture_review.md)  
**基线：** `main` @ `82ddb3322`  
**本审计 tip：** 见当时 PR；零生产代码改动

**事后更正：** 这四张票已合。夹具红后来由 #965 收。不要把本页当时的 Major 当成现在还开着的门。

| 任务 | PR | tip | 实现方声称 | 本轮结论 |
|------|----|-----|------------|----------|
| S1 自己调自己会杀进程 | [#944](https://github.com/MightyBubble/Ludots/pull/944) | `cd84fdf00` | 登记时拒环；漏到运行期抛错，不崩进程 | **可关单** |
| S2 写进去的数会变回去 | [#946](https://github.com/MightyBubble/Ludots/pull/946) | `9f23483a4` | 直写永远存活，和夹不夹上限无关 | **合入，不关单** |
| S4 回滚自己会抛 | [#943](https://github.com/MightyBubble/Ludots/pull/943) | `341f8f5db` | 回滚必须走完；缺服务报错，不装成零目标 | **合入，不关单** |
| S5 容量到顶不说话 | [#945](https://github.com/MightyBubble/Ludots/pull/945) | `f720eb7aa` | 满了就抛；假计数整套删掉 | **可关单**（旁系死计数记债） |

交接里已经点名、本轮核实成立的两处：

- S2 的守卫只扫 Core，展厅侧还有裸写
- S4 的提交写入还没跟 S2 对齐

S3 / S6 / S7 仍在 [#941](https://github.com/MightyBubble/Ludots/pull/941)，本报告不给那张票结论。S9 等 #944 合进主线再开——S1 本轮认定为可关，这条依赖可以解除。第二批（S8 / S10 / S11 / S15）不在本轮范围。

---

## 1. 概述

### 1.1 合并结论

**Verdict：FIX-FORWARD。** 第一批四张票都可以合。S1 和 S5 可以盖关单章。S2 和 S4 的主故障已经修掉，但任务书里的收口还没走完，不能宣称「属性权威」或「事务收尾」已经关单。

四张票都没有重写骨架，也没有用「把容量调大 / 给所有属性都夹上限 / 捕获栈溢出」这类被任务书点名禁止的绕法。声称的主结论，对照源码和测试，三条成立、一条半成立：

| 声称 | 是否成立 |
|------|----------|
| 自己调自己的图在登记时被拒，漏到运行期抛错而不是杀进程 | **成立**。三条验收测试都在，且本轮跑绿 |
| 正式接口写进去的当前值，重算之后还在，和夹不夹上限无关 | **重算结束后成立**。聚合过程中会短暂改写当前值，派生图读到的是中间态 |
| 回滚必须走完；服务没接好要报错，不装成零目标 | **成立**。黑板持有者中途被销毁时回滚不抛；扇出缺服务抛 |
| 容量到顶就抛；假计数整套删掉 | **任务书点名的那一套成立**。同族的事件总线丢弃计数还在，恒为 0 |

不能盖关单章的原因不是「主故障没修」，是强制手段和写入面还没收成一条：

| # | 未竟项 | 一句话 |
|---|--------|--------|
| M1 | S2 强制手段只扫 Core | 新文档把「裸写必须 CI 红」改写成只约束 Core；展厅至少 10 个文件仍直接改属性缓冲 |
| M2 | S4 提交写入仍是平行实现 | 提交路径整块赋值属性缓冲，不走正式写入面；脏标记与聚合排队靠另一套手写循环 |

没有阻断项。没有「一行配置杀死进程」或「正式写入被静默丢弃」这种 P0 残留。

### 1.2 与审查 / 计划的衔接

| 审查编号 | 计划任务 | 本轮 |
|----------|----------|------|
| A1 自递归杀进程 | S1 | **关闭** |
| A2 / A3 属性权威随运行时状态翻转 | S2 第一块 | **关闭**（重算结束后） |
| B10–B13 写入面无强制手段 / 非法 id / id 0 / 未 Freeze | S2 第二至四块 | **部分关闭**：id / Freeze / Core 守卫已落地；展厅裸写与守卫范围未关 |
| B1–B4 回滚中途抛、提交内销毁、事务边界、标签绕过 | S4 四块脏点 | **关闭**（销毁选了「放到最后」而不是两拍协议） |
| C3–C5 缺服务静默、Commit 不自洽 | S4 顺带 | **关闭**，除「提交对齐 S2」 |
| B5–B8 / C6 静默丢弃、热路径扩容、死信号 | S5 | **点名项关闭**；事件总线旁系死计数仍在 |

---

## 2. 结构

```text
1 概述：Verdict / 与审查衔接
2 结构（本页）
3 详情：方法、逐票对照、Major / Minor / 债
4 场景：作者与玩家现在会撞到什么
5 边界：覆盖与不覆盖
6 UAT：关单前必须成立的验收（Cucumber）
附录 A 测试证据
附录 B 给收口 Agent 的最短提示词
```

---

## 3. 详情

### 3.1 审计方法

1. 读 `gitbook/contributing/ai-assisted-development.md` 的任务执行决策规范，以及 #942 计划 §S1 / §S2 / §S4 / §S5 全文（含禁止项与 Cucumber）。
2. 四张票各自相对 `main@82ddb3322` 独立分支，互不包含。在独立 worktree 对照源码，不把实现方声称当证据。
3. 交接里已经点名的两处（守卫只扫 Core、提交未对齐 S2）按「待核实」处理，不是按「已成立」抄录。
4. 本轮跑了实现方任务书指定的核心过滤器（见附录 A）。全绿。没有为了变绿而改生产代码。
5. 零生产修复。本报告只下结论。

### 3.2 S1 · #944 — 可关单

**挂载点：** `GraphProgramRegistry.Register` / `ReplaceProgram` 在写入后走 `EnsureNoInvokeCycle`。失败回滚程序体和源码映射（`cd84fdf00`）。配置加载、展厅登记、热改提交都落到这两处。理由写在 `artifacts/gas-composition-gate.md`，说得通。

**装载期拒环：** `GraphYieldPurityValidator` 命中已在遍历栈上的图号时，不再当成「没找到挂起、放行」，而是报 `GAS.GRAPH.ERR.InvokeCycle`。自递归、A→B→A、热改自递归、按函数名走清单的环，都有测试。

**运行期纵深：** `GraphVmLimits.MaxInvokeDepth = 16`。子调用继承 `InvokeDepth` 和整棵树共享的 `TreeSteps`，不再每次 `Execute` 把步数和深度清零。超限抛 `GAS.GRAPH.ERR.InvokeDepthExceeded`。本轮测试 `万一漏到了运行期也要报错而不是消失` 绿。

**顺带：** 每次调用时对子程序做线性扫描找挂起，已改成登记期算一次 `ContainsYield`，运行期 O(1) 查。任务书允许的顺带项做了。

**未把关单卡住的缺口（记 Minor）：**

- `Register` / `ReplaceProgram` 不解析函数名边（`allowMissingTargets: true`，无 resolver）。按函数名成环的图，要等清单加载器或热改分类补上。生产启动顺序是「先登记图、再加载清单」，这条路径是关的；只调 `Register`、不走清单加载器的测试/工具路径，要靠运行期深度上限兜。
- A→B→A 失败时 A 留在表里、B 回滚。语义正确，但作者会看到「半套环」。

**禁止项核对：** 没有禁止脚本图调用脚本图；没有只做运行期上限；没有尝试捕获栈溢出。

### 3.3 S2 · #946 — 合入，不关单

实现方选了任务书路径 (a)：直写永远存活，聚合器只重算有效上限。正式文档写在 #946 的 `gitbook/architecture/attribute-write-authority.md`，与代码意图一致。

**第一块（权威）——重算结束后成立。**

原先那条按「有没有夹上限、此刻有没有聚合修正」决定写进去的数活不活的分支删掉了。重算结束后，无论夹不夹上限，正式接口写成 50、挂着 +18 修正，当前值仍是 50，有效上限是 118。缺的那一格（无约束 + 活跃聚合 + 直写）有测试 `UnconstrainedAttribute_DirectCurrentWriteSurvivesActiveAggregation`，夹上限对照格也有。

`SetCurrent` / `SetBase` 在值真的变了、且实体已有 `ActiveEffectContainer` 时排队 `AttributeAggregateDirty`。文档把这个条件写进去了。没有单独测试「写完一定挂上脏标记」。

聚合过程中仍会先把当前值改成「基础值 + 修正」，派生图在这一段读到的是中间态，算完再把正式写入填回去。稳态语义是 (a)；过程中不是。文档没写这一句。

**第二块（强制手段）——Core 成立，展厅不成立。这是不关单的主因。**

`ArchitectureGuardTests.AttributeBufferWrites_MustComeFromWhitelistedCallers` 用既有 IL 扫描，白名单是结算 / 聚合 / 装载物化 / 每帧脉冲 / 基准。扫描范围是 `typeof(AttributeBuffer).Assembly`，也就是 **只扫 Core**。本轮这条守卫测试绿。

任务书原文要求「调用方必须在白名单内」，验收是「不在名单里的裸写，CI 必须红」。新文档把范围改成「Core 里只有白名单类型可以调用」。这是用改合同来迁就实现。

展厅侧仍直接改属性缓冲（本轮 `rg` 核实）：

| 文件 | 行为 |
|------|------|
| `UiPlayerAggregateGraphMvpRuntime.cs` | `SetBase` ×2 |
| `ItemSystemShowcaseRuntime.cs` | `SetBase` ×5 |
| `GenreInfoShowcaseRuntime.cs` | `SetBase` / `SetCurrent` |
| `GoldMarketRuntime.cs` | `SetCurrent` ×2（含运行期补金，不是装载） |
| `FourXAssociationRuntime.cs` | `SetCurrent` |
| `LiveSkillWorkbenchVignetteRuntime.cs` | `SetBase` / `SetCurrent` |
| `GraphOpsQueryRuntime.cs` | `SetBase` |
| `GraphOpsAttrRuntime.cs` | `SetBase` / `SetCurrent` ×10 |
| `VisualBenchmarkRuntime.cs` | `SetBase` / `SetCurrent` |
| `GasBenchmarkModEntry.cs` | `SetBase` / `SetCurrent` |

同目录里 `GraphOpsNodeActorBinding` 和 `GraphOpsStageVisuals` 已经改走正式写入面。展厅自己都不统一。金币市场那种运行期裸写，正是任务书说「约定已经失败过一次」的那种。

Core 七处里，兑换和输入绑定已改走正式写入面；其余在白名单上（装载物化、每帧脉冲、基准）。`EffectPhaseSideEffectTransaction` 也在白名单上——这让 S4 的平行提交写入合法通过守卫。

**第三块（非法 id）——成立。**

`SetCurrent(-1)` / `GetCurrent(-1)` / `SetCurrent(64)` 抛 `ArgumentOutOfRangeException`。按名字取不到再写，写入抛；`RequireId` 点名属性名。容量常量收成 `AttributeRegistry.MaxAttributes` 一处。测试 `InvalidAndOutOfRangeAttributeIds_FailClosed`、`RequireId_UnknownName_ThrowsAndNamesTheAttribute` 绿。

实体已死或没有属性缓冲时，正式写入面仍静默返回。这是主线原样，不是本票引入，也不在非法 id 条款里。记债。

**第四块（id 0 与 Freeze）——成立。**

任务书点名的 `attributeId > 0` 生产调用点已改用 `InvalidId` / `IsValidId`。`FirstRegisteredAttribute_ApplyForceWritesIdZero` 绿。`GameEngine` 生产装载结束调用 `AttributeRegistry.Freeze()` 与 `AttributeSinkRegistry.Freeze()`。

### 3.4 S4 · #943 — 合入，不关单

事务骨架没有重写。没有「尽量回滚、失败记日志」。

| 脏点 | 结论 | 证据 |
|------|------|------|
| 回滚自己会抛 | **关闭** | 黑板三缓冲与取消效果恢复都加了存活/组件守卫；`RollbackWorldWrites_WhenBlackboardHolderIsDestroyed_CompletesRemainingRestores` 绿 |
| 提交内销毁不可逆 | **关闭**（选了「销毁放到最后」） | `LandStagedDestroys` 在 `End()` 之后；任务书允许这条，也更推荐两拍协议 |
| 事务边界小于结算 | **关闭**（选了「挂载是可见中间态」） | 阶段注释与测试固定：激活失败不再靠补偿函数偷偷摘掉；切片复位才回收 |
| 标签授予绕过事务 | **关闭** | `StageGrantedTagGrant` 进暂存；`PrepareGrantToEntity` 已删 |

顺带：表现事件缺缓冲抛；三个扇出内置缺服务抛、不再返回零目标；`PrepareCommitState` 进了 `try`，`Commit` 自洽。对应测试绿。

**不关单的主因：提交写入仍是平行实现。** `Commit()` 仍是整块赋值 `AttributeBuffer` + 手写脏位循环，不走 `AttributeMutationOps`。聚合脏标记靠 `PrepareCommitState` 另写一套结构命令。任务书原文：「如果 S2 已经动了属性写入面，这里要一并对齐。」S2 的白名单把这个事务类放行了，所以两边合在一起也不会红。交接已经点名，本轮核实成立。

验收「一次命中多个目标、其中一个被销毁、回滚完整」只有单实体黑板销毁的单元测试，没有多目标扇出集成测试。不够把关单卡住，但不够宣称 Cucumber 原文已测到。

S4 删掉挂载补偿之后，`_activeEffectAttachDropped` 再没有自增，却仍往预算上加。这是 S5 刚清掉的同一种「有计数没写入方」。记 Minor，不要在 S4 里顺手扩范围去改——报告即可。

### 3.5 S5 · #945 — 可关单

| 违规类 | 结论 |
|--------|------|
| 延迟触发器双缓冲皆满静默丢弃 | **关闭**。抛 `GAS.DEFERRED_TRIGGER.ERR.CapacityExceeded`，点名来源与容量。熔断 getter 整套删除 |
| 关系查询静默截断 | **关闭**（图 VM 边界）。`dropped > 0` 且未显式允许截断 → `GAS.GRAPH.ERR.RelationshipQueryIncomplete`；允许截断时丢掉的数量进整数寄存器。照抄空间查询策略，没有去抄扩容 |
| 生成实体热路径 `Reserve` 扩容 | **关闭**。三处改为 `RequireAvailable`，不够就抛。`Reserve` 方法还在，生产零调用 |
| 假计数 | **点名项关闭**。`EffectRequestQueue.DroppedCount` / `_budgetFused`、延迟触发器熔断位、扇出 `DroppedCount` / `AddDropped`、预算上的对应字段，整套删除，不是接线 |

`EffectRequestQueue.Clear` 改成真清空（含溢出环）。展厅换图/每拍清理依赖的是「丢掉待处理」，与真清空兼容。

**不挡关单的缺口：**

- `GameplayEventBus.DroppedEventsLastUpdate` 在 `Publish` 改为超限即抛之后再没有自增，调度系统仍去读它、加到预算上，恒为 0。同族死信号，不在 S5 点名清单里。记债，留给假防线整治（S8）或单独收口。
- 没有「生成系统 + 队列已满」的集成测试；队列本身的超限测试在。
- 关系查询「允许截断」运行期认，作者 JSON 前门目前拒写。作者今天写不出「允许截断」，默认就是必须完整——比任务书更严，不是开口。

**禁止项核对：** 没有用调大容量代替失败关闭；点名的「有 getter 无写入方」已删。

### 3.6 Major / Minor / 债

#### Major（挡住关单，不挡住合入）

| # | 项 | 票 |
|---|----|----|
| M1 | 属性缓冲裸写守卫只扫 Core；展厅至少 10 个文件仍直接写；新文档把验收范围缩到 Core | S2 |
| M2 | 效果阶段事务提交仍平行重实现正式写入面，不走 `AttributeMutationOps` | S4（依赖 S2 已选路径 a） |

#### Minor

| # | 项 | 票 |
|---|----|----|
| m1 | `Register` 不解析函数名边，环检测靠清单加载器补 | S1 |
| m2 | 聚合过程中当前值会被中间态覆盖，派生图读到的不是正式写入；文档未写 | S2 |
| m3 | 正式写入面在实体已死或没有属性缓冲时静默返回（主线原样） | S2 |
| m4 | 没有「`SetCurrent` 之后一定挂上聚合脏标记」的直接测试 | S2 |
| m5 | 回滚验收没有多目标扇出集成测试 | S4 |
| m6 | 销毁放在提交最后，未走引擎两拍销毁协议（任务书允许） | S4 |
| m7 | `_activeEffectAttachDropped` 成为新的死计数 | S4 |
| m8 | 没有生成系统容量预检的集成测试；`TagCountChanged` 超限抛了但没测 | S5 |

#### 债

| # | 项 |
|---|----|
| d1 | `GameplayEventBus.DroppedEventsLastUpdate` / `_droppedInNext` 恒 0，预算仍读它。S8 假防线整治应收 |
| d2 | `EffectRequestQueue.Reserve` 仍公开，热路径已不用 |
| d3 | 测试辅助 `RtsStrategicShowcaseAcceptanceTests.TryAddTrackedAttribute` 仍用 `attributeId > 0` |
| d4 | S3 / S6 / S7 仍在 #941，本轮未审 |
| d5 | S9 依赖 S1 合进主线；本轮认定 S1 可关 |

### 3.7 合同逐条（Cucumber 原文）

| 场景 | 结果 |
|------|------|
| S1 自己调自己必须被拒绝 | **过** `自己调自己的图必须被拒绝` |
| S1 绕一圈回来也算环 | **过** `绕一圈回来也算环` |
| S1 漏到运行期报错不消失 | **过** `万一漏到了运行期也要报错而不是消失` |
| S2 直写语义与夹上限无关 | **过** 无约束 / 夹上限两格 |
| S2 写错属性名失败关闭 | **过** |
| S2 第一个注册的属性可用 | **过** |
| S2 裸写必须 CI 红 | **Core 过，展厅不过** |
| S4 目标中途消失回滚完整且不抛 | **单元过，多目标集成缺** |
| S4 缺服务抛、不装零目标 | **过** |
| S5 延迟触发器满了抛 | **过**（属性 / 标签；标签计数实现有、测试无） |
| S5 关系查询捞不全抛，允许截断可读丢掉数量 | **过**（作者面目前写不出允许截断） |
| S5 生成预检容量不够抛 | **队列层过，生成系统集成缺** |

---

## 4. 场景

**作者写了一张自己调自己的图。** 以前：游戏进程直接没了。现在：登记失败，报调用环；万一漏进去，跑到第 16 层调用会抛，进程还在。

**策划给一个不夹上限的属性写了 50，身上还挂着 +18 的永久修正。** 以前：过一会儿不相干的重算把它改回 118，没有报错。现在：还是 50，有效上限是 118。夹上限的血量同样如此。

**展厅或玩法 Mod 直接改属性缓冲。** 以前：没人拦。现在：Core 里会让 CI 红；展厅里仍然没人拦。金币市场补金、属性家族展厅开局写血，都还是直接改。玩家在展厅看到的数字，不一定走结算。

**一次结算中途有人被销毁。** 以前：回滚可能自己抛，后面的黑板 / 关系不恢复。现在：回滚走完。服务没接好会抛，不会假装「打了零个目标」。

**一帧里触发器或关系查询超过容量。** 以前：多出来的静默消失，面板数字还对。现在：抛，并说出容量和来源。关系查询默认必须捞全。

---

## 5. 边界

**本审计包含：** #944 / #946 / #943 / #945 相对 `main@82ddb3322` 的实现与测试，对照 #942 计划 §S1 / §S2 / §S4 / §S5。

**不包含：**

- #941 上的 S3 / S6 / S7（查询方言开口、退役展厅锁门、展厅血条名实）
- 第二批 S8 / S10 / S11 / S15，以及依赖 S1 的 S9
- 四张票互相 rebase / 合并后的集成（它们目前是四条平行分支）
- 把展厅裸写改掉、或把 S4 提交对齐 S2（那是收口票，不是本审计）
- UI 面板债、表现层改名、#723 评分预算
- 真机 / 展厅游玩（本轮是架构对照 + 指定过滤器测试）

**不要因为本报告去改别人正在开的第二批。** 债务只报告。

---

## 6. UAT

```gherkin
Feature: 第一批修完之后，引擎不再因为一份图或一次写入而说谎或消失
  作为维护者
  我希望这四张票的主故障是真关了
  并且没关完的收口不会被写成「已关单」

  Scenario: 自己调自己的图不能把游戏弄没了
    Given 我写了一张脚本图，它唯一做的事就是调用自己
    When 这张图被登记
    Then 登记必须失败并告诉我这里有一个调用环
    And 游戏进程必须还在

  Scenario: 写进属性的数字在重算后还在
    Given 一个基础值 100 的属性，挂着永久 +18 的修正
    And 我通过正式接口把当前值写成 50
    When 属性重算发生
    Then 当前值仍是 50
    And 这件事与该属性是否夹上限无关

  Scenario: 展厅直接改属性不能再假装有人看门
    Given 一段展厅代码直接调用属性缓冲的写方法
    And 它不在允许名单里
    When CI 跑架构守卫
    Then 守卫必须失败并点名这个调用方
    # 本轮：Core 成立，展厅不成立。S2 因此不能关单。

  Scenario: 结算中途有人消失，回滚仍然完整
    Given 一次命中打了多个目标
    And 其中一个目标在结算过程中被销毁
    When 这次结算失败并回滚
    Then 已经发生的改动都必须被撤销
    And 回滚过程本身不得抛出

  Scenario: 容量到顶必须说话
    Given 一帧内的属性变化触发器超过了队列容量
    When 主缓冲与溢出缓冲都已满
    Then 系统必须抛出并点名容量与来源
```

关单门槛：

- S1 / S5：本轮认定可以关。
- S2：必须先让「裸写 CI 红」对展厅也成立，或把展厅运行期裸写迁到正式写入面并扩大守卫扫描范围。禁止再靠缩文档过关。
- S4：必须让提交路径走与 S2 同一套正式写入面（或证明暂存副本 + 整块提交与正式面语义逐项等价，并有测试锁住）。禁止只把事务类留在白名单上当对齐。

---

## 附录 A — 测试证据

本轮在各票独立 worktree、`-m:1` 下跑指定过滤器，全部 Passed：

| 票 | 过滤器 | 结果 |
|----|--------|------|
| #946 S2 | `AttributeAggregatorTests` + `ExchangeRuntimeTests` | 38 / 38 |
| #946 S2 | `ArchitectureGuardTests.AttributeBufferWrites` + `Issue250` | 2 / 2 |
| #944 S1 | `GraphInvokeCycleTests` + `GraphFunctionCatalogLoaderTests` + `LiveGasEditPipelineTests` | 32 / 32 |
| #943 S4 | `InstantEffectTransactionTests` | 16 / 16 |
| #945 S5 | `GraphFailFastAndCapacityTests` + `DeferredTriggerCollectionTests` | 29 / 29 |

日志：`/opt/cursor/artifacts/s2_946_aggregator_tests.log`、`s2_946_guard_tests.log`、`s1_944_cycle_tests.log`、`s4_943_txn_tests.log`、`s5_945_capacity_tests.log`。

没有写「验证今天会崩」的探针（任务书禁止：那种测试会杀掉 test host）。S1 的正向测试已经覆盖「修好之后应当抛」。

---

## 附录 B — 给收口 Agent 的最短提示词

只开这两张收口票。不要顺手改 S3 / S6 / S7 / 第二批。

### B.1 S2 强制手段收到展厅

```text
对照 docs/audits/s_batch1_architecture_audit.md M1。
S2（#946）路径 (a) 已经成立，不要改聚合器语义。

要做：
1) 把 ArchitectureGuardTests.AttributeBufferWrites 的扫描范围从
   typeof(AttributeBuffer).Assembly 扩到 mods/ 里会打进 CI 的程序集
   （至少 showcases 与 GasBenchmarkMod / PerformanceVisualizationMod）。
2) 展厅运行期裸写改走 AttributeMutationOps（GoldMarketRuntime 补金、
   FourXAssociationRuntime 开局金、GraphOpsAttrRuntime 等）。
   装载物化如果要留 SetBase，必须进白名单并在文档点名「仅装载」。
3) 回写 attribute-write-authority.md：守卫扫谁、白名单是谁，
   禁止再写成「只约束 Core」。
4) 补一条会红的守卫测试：在测试用的非白名单类型里调用 SetCurrent，
   断言失败文案点名调用方。现有 Core 守卫保持。

禁止：删展厅写入来让守卫变绿；把展厅排除出扫描当解决；改路径 (a)。

该跑：ArchitectureGuardTests + AttributeAggregatorTests + 被改展厅对应的验收测试。
```

### B.2 S4 提交写入对齐 S2

```text
对照 docs/audits/s_batch1_architecture_audit.md M2。
S4（#943）回滚 / 缺服务 / 挂载中间态不要重做。

要做：
让 EffectPhaseSideEffectTransaction.Commit 的属性段与
AttributeMutationOps 同一套语义（脏位、聚合排队、表现变更位、
非法 id 失败关闭）。可以让暂存副本走 SetCurrent/SetBase，
提交时复用正式面；不要再手写第二套循环冒充对齐。

补测试：事务提交后的当前值，在「无约束 + 活跃聚合修正」下
重算后仍然存活（与 S2 那一格同一断言）。

禁止：重写事务骨架；把 EffectPhaseSideEffectTransaction 留在
S2 白名单上当作已经对齐。

该跑：InstantEffectTransactionTests + AttributeAggregatorTests。
```
