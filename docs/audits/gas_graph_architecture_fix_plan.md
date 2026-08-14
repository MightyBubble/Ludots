# GAS + Graph VM 架构修复计划（Epic + 子任务分配）

**依据：** [GAS + Graph VM 架构审查](gas_graph_architecture_review.md)（当时结论）
**当时 tip：** `origin/main` @ `82ddb3322a`
**现在怎样：** [图能力唯一入口](../../gitbook/architecture/graph-capability-status.md)
**用途：** 当时用来开 GitHub Epic 与子 issue。S 计划子 issue 没建成，活是直接用 PR 做的。不要再跑建票脚本当现状。
**批量创建：** `scripts/create-gas-graph-fix-issues.sh`（不要再 `--apply`）

**事后更正（主干 `d1b8f5f4d7`）：** S1–S13、S15 玩家/作者门已合。S6 从「锁退役门」变成「房间删除」。S14 只合了脚手架。#916 / #917 / #918 还开着。合同仍是修复中。

---

## 1. 概述

架构审查的结论是「骨架比收尾好得多」：图 VM 执行核心、编译器前端、组件布局、零分配纪律都是认真做的，不需要重做。因此本计划**不包含任何重写**，全部是收口。

十四个子任务分四档。P0 三条是「今天就能出事」：一条会杀进程，一条让属性数值不可信，一条让技能结算能混进可挂起动作。P1 四条是「已经在骗人」：事务的回滚本身会失败、容量到顶不说话、退役的门没锁、血条名不副实。P2 六条是「地基没夯实」：分层前门没人走、表现层在决定玩法、假防线成片、寄存器索引无归属。P3 一条是唯一的大工程：把层拆成真程序集。

**先修哪个**：S1 → S2 → S3 三条可以同时开三个 Agent，互不冲突。其余按 §2 的依赖图排。

---

## 2. 结构

### 2.1 优先级与并行性

| 档 | 子任务 | 可并行 | 依赖 | 预估触碰面 |
|----|--------|--------|------|------------|
| **P0** | S1 图调用无界递归 | 是 | — | VM 1 处 + 校验器 1 处 + 新测试 |
| **P0** | S2 属性写入权威统一 | 是 | — | 属性核心 + 7 处 Core 调用方 + 守卫 |
| **P0** | S3 查询方言的可挂起动作开口 | 是 | — | 编译器 1 个分支 + 测试参数 |
| **P1** | S4 事务收尾：回滚不可失败 | 是 | — | 事务类 4 个循环 + 销毁语义 |
| **P1** | S5 容量三禁：丢弃 / 扩容 / 死信号 | 是 | — | 4 个队列 + 3 处 Reserve |
| **P1** | S6 退役展厅锁门 + 停止清表 | 是 | — | 登记表 + 启动器 + 5 个 mod bootstrap |
| **P1** | S7 展厅血条名实相符 | 是 | — | 画廊 driver + 分镜数据 + 文案 |
| **P2** | S8 假防线集中整治 | 是 | — | 纯测试改动 |
| **P2** | S9 L2 走 L1 正式前门（引入 GraphFrame） | 否 | **S1** | VM 对外 API + 3 个 L2 宿主 |
| **P2** | S10 表现层不得 gate 玩法选中 | 是 | — | 2 个输入系统 + 新守卫 |
| **P2** | S11 覆盖表错误归因 + 守卫强度 | 是 | — | 生成器 + 覆盖表守卫 |
| **P2** | S12 寄存器归属 + 指令 descriptor | 否 | **S9** | 编译器 + 前门矩阵 |
| **P2** | S13 Script 方言拓宽 | 否 | **S9 + S12** | 能力矩阵 + 宿主 state 合同 |
| **P3** | S14 分层物理化（拆程序集） | 否 | 建议在 S9/S12 后 | 全仓 csproj 结构 |
| **chore** | S15 验收页 SSOT 合并（红灯部分已修） | 是 | — | 一份文档 |

### 2.2 依赖图

```text
S1 ──────────────► S9 ──────► S12 ──────► S13
                    │           │
                    └───────────┴────────► S14（建议，非硬依赖）

S2  S3  S4  S5  S6  S7  S8  S10  S11  S15   ← 全部可独立并行开工
```

**唯一的硬顺序**：S13（拓宽 Script 方言）必须在 S9（宿主拿到完整执行帧）之后。反了必崩——今天三个 L2 宿主只填了部分寄存器就能跑，正是因为 Script 方言窄到用不到那些寄存器。

### 2.3 不在本计划内

UI 面板债（#886 / #893）、表现层改名、#723 评分预算、以及「删掉八个家族 Mod」（那是 S6 关门之后的独立票）。

---

## 3. 详情（子任务任务书）

> 每个子任务的「给 Agent 的任务书」可整段复制。共同纪律，不再逐条重复：
> 先读 `gitbook/contributing/ai-assisted-development.md` 的任务执行决策规范；
> NO FALLBACK / SSOT / DRY / SOLID / DDD；数据驱动、NO HARDCODE；
> ECS：SoA、0 Alloc、chunk 迭代、inline query、span、command buffer；禁止热路径结构变更、禁止内存飞线；
> 只改自己这一条的范围，不顺手改别的；不重开已裁决的产品争论；
> 禁止发明不存在的类型或方法（每个引用先搜索确认）。

---

### S1 · 图调用无界递归会杀进程 【P0】

**审查编号：** A1

**任务书**

```text
目标：让一张自己调自己（或成环）的图在登记时就被拒绝，并让运行期的失控有硬上限。
这是当前唯一「一行配置杀死进程」的问题，优先级最高，改动面小且独立。

现状（已实测，不要再花时间复现）：
  注册一张脚本图，程序体只有一条 InvokeScript 指向自己 →
  GraphProgramRegistry.Register 接受，TryGetProgram 返回 True（无装载期环检测）；
  执行时递归 1495 层后栈溢出，进程退出码 134，try/catch 无法进入
  （.NET 的 StackOverflowException 不可捕获）。

三道看起来相关的闸门都不管这件事，别误以为它们能兜住：
  1) GraphVmLimits.MaxCallStackDepth = 16 只约束同一程序内的 Call，
     子调用拿到的是全新 CallStack 且 CallStackCount = 0；
  2) MaxInstructionsPerExecution = 4096 是 per-Execute 预算，
     GasGraphOpHandlerTable.Execute 每次新建 cursor 且 Steps = 0 —— 预算不复合，
     实际上限是 4096 的深度次方；
  3) GraphYieldPurityValidator 是唯一遍历 InvokeScript 边的组件，
     它遇到环时是 if (!activeGraphs.Add(graphId)) return false;
     —— 把环当作「没找到 Yield」放行，而不是判错。

要做三件事：
  1) 装载期拒环。复用 GraphYieldPurityValidator 已有的 activeGraphs 栈，
     把「命中环」从 clean 改成 error（新错误码，形如 GAS.GRAPH.ERR.InvokeCycle，
     沿用该文件既有的诊断风格，不要发明新的错误体系）。
     注意它今天只挂在 GraphFunctionCatalogLoader 与 LiveGasEditPipeline 两处；
     环检测必须覆盖所有 InvokeScript 来源，最自然的挂载点是
     GraphProgramRegistry.Register / ReplaceProgram，或 GraphProgramConfigLoader.PatchAndRegister。
     选哪个都行，但要在 PR 里说明为什么，并确保热改路径也过这道检查。
  2) 运行期深度上限 + 共享步数预算。把 invoke depth 与「整棵调用树共享一个步数预算」
     放进 GraphExecutionCursor（或等价位置），超限抛，不要静默截断。
     这条是纵深防御：即使装载期漏了，运行期也必须是抛错而不是杀进程。
  3) 补测试。今天零覆盖：rg "Recursi|SelfInvoke|Depth" 在 src/Tests/GasTests/Graph
     只命中 MaxCallStackDepth 的 stackalloc。至少三条：
     自递归拒绝、A→B→A 环拒绝、深度超限时抛（而非崩）。
     注意：如果你要写一条「验证今天会崩」的测试，它会杀掉整个 test host，
     必须隔离或直接不写——写「修好之后应当抛」的正向测试即可。

顺带（同一处，成本几乎为零）：
  RequireNoYield 目前在每次 InvokeScript 运行时对子程序做 O(n) 线性扫描找 Yield，
  这本该在装载期做一次。你已经在动装载期校验了，可以一并挪过去。

禁止：
  用「限制 Script 图不许 InvokeScript」来回避问题 —— 那会砍掉 FuncLib 复用；
  只加运行期深度上限而不做装载期拒环（作者应该在登记时就知道）；
  把栈溢出改成捕获（.NET 里做不到，别试）。

验收：
  Feature: 一张图不能把游戏弄没了
    Scenario: 自己调自己的图必须被拒绝
      Given 我写了一张脚本图，它唯一做的事就是调用自己
      When 这张图被登记进图注册表
      Then 登记必须失败并告诉我这里有一个调用环
      And 游戏不应该能带着这张图启动

    Scenario: 绕一圈回来也算环
      Given 图 A 调用图 B，图 B 又调用回图 A
      When 它们被登记
      Then 登记必须失败并指出这条环上的图

    Scenario: 万一漏到了运行期，也要报错而不是消失
      Given 一条嵌套调用链深过了引擎允许的深度
      When 它被执行
      Then 必须抛出并说明深度上限
      And 进程不得退出
```

**该跑：** `--filter "FullyQualifiedName~GraphFunctionCatalogLoaderTests|FullyQualifiedName~GraphActionCatalogLoaderTests|FullyQualifiedName~LiveGasEditPipelineTests|FullyQualifiedName~GraphExecuteSliceBudgetTests|FullyQualifiedName~GraphContractTests"`

---

### S2 · 属性写入的权威与强制手段 【P0】

**审查编号：** A2、A3、B10、B11、B12、B13

**任务书**

```text
目标：让「我写进属性的数字就是最终生效的数字」成立，并让「只有结算能改属性」有强制手段。
这是 GAS 的地基。建立在「属性值可信」之上的一切（伤害结算、触发条件、存档、回放）
都在这条裂缝之上。

现状（已实测，三格对照）：
  基础值 100 的属性 + 一个永久 +18 的聚合修正，然后用正式接口 SetCurrent(50)：
    属性夹上限（血量这类）+ 重新标脏 → 重算后 50   （写入存活）
    属性不夹上限（移速这类）+ 重新标脏 → 重算后 118 （写入被静默丢弃）
    属性不夹上限 + 不重新标脏         → 重算后 50   （暂时存活）
  代码依据：AttributeAggregatorSystem.RestorePersistentCurrentValues
    if (!touchedByAggregation || clampsToEffectiveCap) SetCurrent(i, previousCurrentValues[i]);
  而 AttributeMutationOps.SetCurrent 不排 AttributeAggregateDirty，
  所以覆盖时机与写入完全脱钩：值可存活任意多帧，直到不相干事件把实体标脏才被抹掉。

要做的事，按四块，可以拆成四个 PR 但建议同一个 Agent 连续做（它们互相牵连）：

一、把 current 的权威定下来并写进文档
  今天「谁是权威」取决于运行时状态（属性有没有配约束、那一刻有没有活跃聚合修正），
  这是不可接受的。裁决权在维护者，但实现上必须做到「同一个 API 语义唯一」。
  两条可选路径，选一条并在 PR 里写明理由：
    (a) 直写永远存活：聚合器只负责算「有效上限」，不回写 current；
        需要重算 current 的场景改为显式操作。
    (b) 直写永远不存活：把「直接改当前值」从公开 API 收回，
        只留「申请一次属性变更（走结算）」，聚合器是唯一 current 写者。
  不管选哪条，SetCurrent 必须排 AttributeAggregateDirty ——
  「写了之后要过多久才被覆盖」不能是不确定的。
  然后补上那唯一缺失的守卫格：无约束属性 + 活跃聚合修正 + 直写 current。
  现有 AttributeAggregatorTests 覆盖了四种组合里的三种，唯独缺会丢数据的这一格。

二、给写入面装强制手段（现在是零）
  AttributeBuffer 的 SetBase/SetCurrent/SetAggregatedCurrent 全是 public，
  AttributeMutationOps 是 public static，无 internal 收口、无 analyzer 工程、无守卫测试。
  Core 自己就有 7 处裸写（ExchangeRuntime、InputActionAttributeBindingSystem、
  ForceInput2DSink、CameraBehaviorInputSink、ComponentRegistry、
  TemplateEntityBatchSpawner、QuestDefinitions），mod 侧 10+ 处。
  所以这不是使用者的错，是架构没给手段。
  仓库里已有现成范式，不要发明新东西：
    - 围栏范式：GasGraphRuntimeApi 的 BeginDerivedAttributeWrites /
      EndDerivedAttributeWrites / RejectDerivedAttributeSideEffect
      已经证明「scope 独占 + 暂存 buffer + commit-or-discard + 越权即抛」在本代码库可行。
    - 守卫范式：ArchitectureGuardTests 已有 IL 级调用扫描基建
      （EnumerateCalledMethods，GasAbilityExecHotPath_DoesNotCallWorldAddOrRemoveDirectly 在用）。
      把它的黑名单从一个类型扩到「AttributeBuffer 的写方法调用方必须在白名单内」，
      是既有工具的直接复用。
  注意有一条守卫测试目前反过来把绕行钉成了契约：
    ArchitectureGuardTests.Issue250_ExchangeAttributeCostInput_UsesGasAttributesAndShowcaseMod
    里有 Assert.That(runtime, Does.Contain("attributes.SetCurrent"))。
    动 ExchangeRuntime 必须同时改这条断言。

三、非法与越界 id 必须失败关闭
  实测：SetCurrent(-1)、GetCurrent(-1)、SetCurrent(64) 全部静默返回不抛。
  而按名字取 id 找不到时返回的正是 -1 —— 于是「取一个没注册的属性名然后写它」
  这条常规路径会静默吞掉整次写入，连脏标记都不打，零异常零日志。
  参照同族的 GameplayTagContainer.ValidateTagId（对非法 id 抛 ArgumentOutOfRangeException）。
  上限 64 在三处独立定义（AttributeBuffer / AttributeRegistry / DirtyFlags），
  顺手收成一处引用。

四、统一两个注册表的有效性约定，并把属性表 Freeze 起来
  属性表：_nextId = 0，InvalidId = -1  → id 0 合法
  标签表：_nextId = 1,  InvalidId = 0  → id 0 非法
  有 6+ 处代码套用标签表的约定去判属性（if (attributeId > 0)），
  于是第一个注册的属性在这些路径上静默不可用，而「第一个」取决于 mod 加载顺序。
  已知调用点：BuiltinHandlers.HandleApplyForce（4 处）、NarrativeDirector、
  AbilityExecLoader（3 处）、UtilityAiRuntimeEvaluator。
  同时：AttributeRegistry.Freeze() 与 AttributeSinkRegistry.Freeze() 生产从不调用，
  而 ProgressionIdRegistry / ContextGroupIdRegistry / GraphIdRegistry 都规规矩矩 Freeze 了。
  id 分配依赖 mod 加载顺序会威胁存档与回放的确定性，
  gas-layered-architecture.md 也明文要求「在一个注册点统一 Register 并最终 Freeze」。

禁止：
  用「给所有属性都配上夹上限约束」来绕过第一块 —— 那是把 bug 变成约定；
  只改 mod 侧调用方而不动强制手段（约定已经失败过一次，失败者包括规则作者自己）；
  为了让守卫测试变绿而放宽断言。

验收：
  Feature: 我写进属性的数字不会自己变回去
    Scenario: 直接写入的当前值语义唯一
      Given 一个基础值 100 的属性，挂着一个永久 +18 的修正
      And 我通过正式接口把当前值写成 50
      When 属性重算发生
      Then 结果必须与该属性是否配了夹上限约束无关
      And 这个语义必须写进正式文档

  Feature: 写错属性名不应该悄无声息
    Scenario: 非法属性编号必须失败关闭
      Given 我引用了一个没有注册的属性名
      When 系统按这个名字取编号，然后拿这个编号去写值
      Then 写入必须失败并点名这个属性名

  Feature: 属性的编号不该看 mod 装载顺序
    Scenario: 第一个注册的属性也必须可用
      Given 某个属性恰好是本次运行中第一个被注册的
      When 内置的施加冲力之类的路径去读它
      Then 它应当和其他属性一样正常工作

  Feature: 绕过结算改属性必须被拦住
    Scenario: 裸写属性缓冲要么编译不过，要么 CI 红
      Given 一段代码直接调用属性缓冲的写方法，而它不在允许名单里
      When CI 跑架构守卫
      Then 守卫必须失败并点名这个调用方
```

**该跑：** `--filter "FullyQualifiedName~AttributeAggregatorTests|FullyQualifiedName~AttributeDerivedGraphTests|FullyQualifiedName~AttributeBindingTests|FullyQualifiedName~DeferredTriggerCollectionTests|FullyQualifiedName~DeferredTriggerTests|FullyQualifiedName~ExchangeRuntimeTests|FullyQualifiedName~AllocationTests|FullyQualifiedName~InstantEffectTransactionTests|FullyQualifiedName~SystemIntegrationTests"`
另跑 `src/Tests/ArchitectureTests/ArchitectureTests.csproj --filter "FullyQualifiedName~ArchitectureGuardTests"`

---

### S3 · 查询方言可从纯管道调起可挂起动作 【P0】

**审查编号：** A4（同 `pr932_graph_landed_architecture_audit.md` B1）

**任务书**

```text
目标：关掉查询方言的可挂起动作开口，让「技能结算不得中途调可挂起动作」这条红线
在所有方言上一致成立。这是作者今天就能写出来的，且零测试覆盖。

现状：
  线性方言（Effect/Score/Validation/Derived）在 GraphControlFlowCompiler.Linear.cs 里
  对 InvokeScript 明确拒绝裸图号：缺 functionName 报 MissingNodeRef，
  给了 graphId 报 TypeMismatch「cannot use graphId in linear FuncLib authoring」。
  查询方言在 GraphControlFlowCompiler.Query.cs 里只拒绝「两个都给」和「两个都不给」，
  只给 graphId 一路放行，CompileQueryNode 原样编译成 Imm = node.GraphId; Flags = 0。
  下游拦不住：能力矩阵把 InvokeScript 静态标为 Pure（与 Jump/Call/Return 同组），
  只看本图不跟进被调图；运行期只校验被调图 kind == Script 且不含 Yield，
  而 action_lib.json 11 条里有 9 条不含 Yield，全部可被一张查询图调起。
  违反 graph-funclib-actionlib-contract.md §3.4 明文禁令。

要做：
  1) 查询方言与线性方言对齐：InvokeScript 只允许 functionName，graphId 直绑拒绝。
     诊断码与文案沿用线性侧，不要发明新错误体系。
  2) 给 GraphEffectAuthoringExpressivenessTests.FrontDoor_LinearKindsInvokeScriptGraphId_FailClosed
     的 [TestCase] 补上 "Effect" 与 "Query"。今天它只覆盖 Score / Validation / Derived。
  3) 先搜 assets/ 与 mods/ 下所有 kind=Query 且用 graphId 的图。
     如果有现存图因此编译失败，失败关闭并在 PR 里报告，
     不要为了让测试变绿而放宽校验。

背景（这条为什么是架构问题而不只是一个漏判分支，供你判断改法用）：
  这套能力模型是「逐指令 + 只看本图」的。把 InvokeScript 标成 Pure，
  等于把「调用」当成控制流而不是能力传递点 —— 而调用的实际能力取决于被调图。
  全仓唯一的跨图分析是 GraphYieldPurityValidator，它只管 Yield 纯度，
  且只挂在 FuncLib 登记与热改两条路上。所以「禁止裸图号、强制一切间接调用
  都经过一本有跨图校验的清单」是这套模型下唯一自洽的收口方式，
  而线性方言已经这么做了 —— 你只是把它推广到全部方言。
  如果你想改成「调用的能力 = 被调图能力的并集」（真过程间分析），
  那是更彻底但更大的改动，请先提方案再动手。

禁止：改合同措辞来迁就实现；新增 opcode；碰线性方言。

验收：
  Feature: 纯查询里不能偷偷跑起会等一拍的动作
    Scenario: 查询图直绑图号必须被拒绝
      Given 我在一张查询图里写了一个调用，直接指向某个巡逻动作的图号
      When 这张图通过作者前门编译
      Then 编译必须失败，理由与线性方言一致
    Scenario: 走清单名字仍然允许
      Given 我在查询图里用纯算式清单里登记的函数名做调用
      When 这张图通过作者前门编译
      Then 编译应当成功
```

**该跑：** `--filter "FullyQualifiedName~GraphEffectAuthoringExpressivenessTests|FullyQualifiedName~GraphQueryControlFlowTests|FullyQualifiedName~GraphContractTests"`

---

### S4 · 事务收尾：回滚必须不可失败 【P1】

**审查编号：** B1、B2、B3、B4、C3、C4、C5

**任务书**

```text
目标：让效果阶段事务的回滚路径无条件成功，并把「事务边界」与「一次结算」对齐。
事务骨架本身是专业的（暂存 + 校验 + 提交 + 回滚，六个外部队列都有检查点），
不要重写它 —— 只补收尾。

四个已定位的脏点：

一、回滚自己会抛（最要紧）
  EffectPhaseSideEffectTransaction.RollbackWorldWrites 里相邻循环风格不一致：
    恢复 ActiveEffectContainer 的循环   → 有 _world.IsAlive && _world.Has 守卫
    恢复 GameplayEffect 的循环          → 有守卫
    恢复 BlackboardFloatBuffer 的循环   → 裸 _world.Get<T>()，无守卫
    恢复 BlackboardIntBuffer 的循环     → 无守卫
    恢复 BlackboardEntityBuffer 的循环  → 无守卫
    恢复 cancelledEffect 的循环         → 无守卫
  这是漏写而非设计选择。阶段执行期间若有实体被销毁，回滚会中途抛出，
  其后的 listener buffer / relation / 结构性移除全部不执行 —— 回滚本身留下部分结果。
  真事务的铁律是回滚路径必须无条件成功。补齐守卫，并补一条测试：
  阶段执行中销毁一个持有黑板的实体，断言回滚完整走完。

二、提交路径内的销毁不可逆
  Commit() 在外部发布之后执行 _world.Destroy(effect) 循环，之后还有 _rootBudget.CommitWrites。
  销毁一旦发生 Rollback 没有任何复活手段（RollbackWorldWrites 所有循环都以 IsAlive 为前提）。
  所以 destroy 之后的任一异常会留下「实体没了、但它的属性/标签/关系被回滚」的混合态。
  选一条：把销毁移到提交的最后一步（之后不允许有任何可失败操作），
  或者把销毁降级成「打标记 + 由 Cleanup 相落地」。后者与引擎既有的
  publish→finalize 两拍销毁协议一致，更推荐。

三、事务边界小于结算边界
  EffectApplicationSystem.UpdateSlice 的 ProcessPending 阶段已经把效果塞进目标的
  ActiveEffectContainer（并可能 World.Add 该组件），这些都在事务之外；
  ActivateEffects 阶段才开事务跑阶段图。两阶段之间可因 MaxWorkUnitsPerSlice 跨帧让步，
  失败时靠 RollbackPersistentAttachment 手工补偿。
  要么把 attach 纳入同一事务，要么明确「挂载是可见的中间态」并补测试固定这个语义。
  不要留在「靠补偿函数拼接」的状态。补一条测试：在两阶段之间截断，断言中间态符合选定语义。

四、标签授予绕过事务
  EffectApplicationSystem 在事务作用域内调 EffectTagContributionHelper.PrepareGrantToEntity，
  后者直接 world.Get<GameplayTagContainer> 改世界、不进暂存，
  靠外层 tagsBefore / tagCountsBefore / dirtyFlagsBefore 三个快照在 catch 里手工还原。
  顺序目前是对的，但这正是边界清单「禁止阶段执行直接绕过事务写入外部系统」所指的形态：
  同一实体的标签有两套写入与两套回滚机制。把它纳入事务暂存。

顺带（同一批，成本低）：
  - StagePresentationEvent 在服务缺失时静默 no-op，而同类 StageEffectRequest /
    StageSpawnRequest 抛异常 —— 同一个类里两种缺服务语义，统一成抛。
  - 三个 fan-out builtin 在 SpatialQueries / FanOutBudget / ResolverBuffer 缺失时
    静默 return 零结果，把「服务没接」降级成「零目标」。改成抛。
  - PrepareCommitState() 在 try 之外调用，Commit 非自洽，全靠三个调用方各自 catch 兜底。
  - Commit 是 AttributeMutationOps 的平行重实现（整 struct 赋值 + 手写 dirty 循环），
    两套语义需人工同步 —— 如果 S2 已经动了属性写入面，这里要一并对齐。

禁止：把回滚改成「尽量回滚，失败就记日志」；重写事务骨架。

验收：
  Feature: 一次结算要么全算，要么全不算
    Scenario: 目标中途消失时回滚仍然完整
      Given 一次命中打了多个目标
      And 其中一个目标在结算过程中被别的效果销毁
      When 这次结算失败并回滚
      Then 所有已经发生的改动都必须被撤销
      And 回滚过程本身不得抛出

    Scenario: 服务没接好要报错，不要装作没有目标
      Given 某个扇出内置需要的服务没有注册
      When 它被执行
      Then 必须抛出并点名缺失的服务
      And 不得返回「命中零个目标」
```

**该跑：** `--filter "FullyQualifiedName~InstantEffectTransactionTests|FullyQualifiedName~EffectPhaseArchitectureTests|FullyQualifiedName~PhaseExecutionPathTests|FullyQualifiedName~PhaseListenerBatchHexTests|FullyQualifiedName~ResponseWindowRobustnessTests|FullyQualifiedName~RootBudgetTests|FullyQualifiedName~EffectCompositionSsotTests"`

---

### S5 · 容量三禁：静默丢弃 / 热路径扩容 / 死信号 【P1】

**审查编号：** B5、B6、B7、B8、C6

**任务书**

```text
目标：让 gas-layered-architecture.md 的「禁止固定容量溢出时静默丢弃、截断或在热路径扩容」
在代码里真的成立，并把整条断线的容量遥测接上。

三类违规，都已定位：

一、静默丢弃
  DeferredTriggerQueue：主缓冲与溢出缓冲皆满时直接 return 丢弃触发器，
  唯一痕迹是 _attributeBudgetFused / _tagBudgetFused / _tagCountBudgetFused 三个标志，
  而这三个标志有 public getter 但**全仓库没有任何读取点**。
  后果：属性/标签变化触发器在高压帧被静默丢弃 —— 面板数字是对的，联动不响，极难定位。
  改成抛，并点名容量与来源。

  图 VM 的关系查询算子静默截断：出边超过 MaxTargets = 256 的实体，多出的目标无声消失
  （RelationshipRuntime.CollectOutgoing 满即 break、只返回 count 不返回 dropped；
  GraphTargetList.SetCount 再截一刀）。
  对照组就在同一个文件里：空间查询算子做得完全正确 ——
  默认 RequireComplete，dropped > 0 且未显式授权就抛 GAS.GRAPH.ERR.SpatialQueryIncomplete，
  AllowTruncated 必须显式声明并把 dropped 写进输出寄存器。
  照抄空间查询那套策略到关系查询算子上。注意 OwnershipResolver 的
  CollectOutgoingOwned/CollectIncomingOwners 用 Array.Resize 重试，
  恰好补上了 CollectOutgoing 缺失的 dropped 语义 —— 可以参考它的信息，但别照抄扩容做法。

二、热路径扩容
  RuntimeEntitySpawnSystem 三处 _effectRequests.Reserve(Count + OverflowCount + N)，
  而 Reserve 是真扩容（换两条更大的数组 + 搬运溢出环，只增不减）。
  这把本该抛的 CapacityExceededError 变成了静默堆增长。
  注意相邻分支写得很对：队列服务缺失时直接抛 —— 说明作者是有意识要保证容量够的，
  只是选的手段恰好是被禁的那个。改成容量断言（不够就抛，让调用方或配置去调容量）。

三、死信号（这一类比缺功能更糟：它让人以为有防护，而测试还在盖章）
  EffectRequestQueue._dropped 全文没有任何自增，DroppedCount 恒为 0，
  而测试里有 Assert.That(q.DroppedCount, Is.EqualTo(0)) —— 恒真的空断言。
  _budgetFused 置位后终身为真、无人读、Clear/ConsumePrefix 都不复位。
  BuiltinHandlerExecutionContext.DroppedCount 的唯一写入者 AddDropped 的入参
  来自 TargetResolverFanOutHelper.ValidateAndCollect 的 ref int dropped，
  而该方法从不给它赋值（容量与预算超限都是直接抛）。
  于是 GasBudget.EffectProposalFanOutDropped 与 EffectApplicationSystem._fanOutDropped 也恒为 0。
  决策：要么真接线（有丢弃就计数并上报），要么整套删掉。
  留着「有 getter 没有写入方」是最坏的选项。同时把那条恒真断言删掉或改成有意义的断言。

顺带：
  EffectRequestQueue.Clear() 是 _count = 0 之后 RefillFromOverflow() ——
  如果主缓冲里还有未消费项会被无声丢弃，且回灌溢出环。
  Core 生产系统不调它（用的是 ConsumePrefix），但 showcase mod 有 4 处在调：
  GraphOpsHeadlessGameEngine（换展厅）、AttrNodeDriver、SandboxNodeDriver、EventNodeDriver
  （后三个每拍都在调）。先确认没有调用方依赖「Clear 后 refill」这个行为，
  再把 Clear 的语义改成真清空（含溢出环与熔断位）。

禁止：用「把容量调大」代替失败关闭；保留任何「有 getter 无写入方」的遥测字段。

验收：
  Feature: 容量到顶时要说话
    Scenario: 延迟触发器队列满时必须失败关闭
      Given 一帧内产生的属性变化触发器超过了队列容量
      When 主缓冲与溢出缓冲都已满
      Then 系统必须抛出并点名容量与来源

    Scenario: 关系查询捞不全时必须失败关闭
      Given 一个实体的关系边数超过了单次查询的上限
      When 一张图对它做关系查询而没有显式允许截断
      Then 执行必须失败并说明被丢掉了多少
      And 显式允许截断时，被丢掉的数量必须能被作者读到

    Scenario: 生成实体时容量不够要报错，不要偷偷扩容
      Given 一批生成请求需要的效果队列容量超过了当前容量
      When 系统预检容量
      Then 必须抛出并说明需要多少
```

**该跑：** `--filter "FullyQualifiedName~GraphFailFastAndCapacityTests|FullyQualifiedName~DeferredTriggerCollectionTests|FullyQualifiedName~DeferredTriggerTests|FullyQualifiedName~GasExecutionBudgetTests|FullyQualifiedName~RootBudgetTests|FullyQualifiedName~PhaseListenerBatchHexTests"`

---

### S6 · 退役展厅锁门 + 停止清空图编号表 【P1】

**事后更正：** 锁门已做（#941），房间已删（#968）。下面任务书是当时写法，不要再按「八条 retired binding」派新票。

**审查编号：** `pr932_graph_landed_architecture_audit.md` B2（本轮 B25 提供了根因）

**任务书**

```text
目标：把八间已宣布退役的家族展厅的门真锁上，并停止在 mod 运行时清空引擎的图编号表。
这两件事单独看都只是 Major，叠在一起才是阻断 —— 玩家点得进去，而进去就破坏核心注册表。

现状：
  登记表这边做干净了：八条 status=retired、preset=null。
  但 binding 八条全留着，launcher.config.json 里八条 binding 全是活的；
  启动器 launchHint() 先看 binding、完全不看 status，
  于是每张退役卡照样吐出可复制的启动命令。
  而 Rel / Query 两间在 GameStart 上对活引擎直呼 BindStandaloneFromModAssets()，
  落到 bootstrap 的 GraphIdRegistry.Clear()；
  Attr / Spatial / Event 三间的 bootstrap 里也有同一句。
  GraphIdRegistry.Clear() 连 _frozen 一起复位、_nextId 归 1，
  而引擎在 init 期已注册全部图编号，实例级 GraphProgramRegistry 仍按旧编号存程序
  —— 同进程两套编号空间打架。

第一步（锁门）：
  1) showcase.registry.json 里八条 capability_standard_graph_ops_* 的 binding 置 null。
     正确先例是 physics2d_playground（退役时 binding 与 preset 双 null + 豁免）。
  2) launcher.config.json 删掉对应八条 binding。
  3) scripts/validate-registry.py 强制 registry binding 与 launcher binding 双向一致，
     所以要按它的 exemptions(kind=binding) 机制补豁免，
     或改规则让 retired 条目允许 binding=null —— 二选一，写清理由，不要留静默特例。
     注意这条规则目前形成了反向激励：退役条目一旦删掉 binding 就会校验失败，
     机制上鼓励「留着门」。改规则是更根本的做法。
  4) 启动器侧：src/Tools/Ludots.Launcher.React/src/lib/showcase.ts 的 launchHint
     与 components/ShowcasePanel.tsx 必须让 status=retired 不再产出可复制的启动命令。

第二步（停止清表）：
  删掉这五处生产 mod 代码里的 GraphIdRegistry.Clear()：
    GraphOpsRelShowcaseBootstrap / GraphOpsQueryCatalogBootstrap /
    GraphOpsAttrGraphBootstrap / GraphOpsSpatialCatalogBootstrap / GraphOpsEventGraphBootstrap
  Rel 与 Query 的 ModEntry 还在 GameEvents.GameStart 上对活引擎调
  BindStandaloneFromModAssets() —— 这条也要断掉。
  清表是测试夹具的需求，不是 mod 运行时的需求；测试侧需要就在测试里做。
  并给那六个未标注的 fixture 补 [NonParallelizable]（今天只有 Rel/Query 标了，
  而五个 bootstrap 都会清进程级静态注册表 —— 这是 CI 串扰风险）。

顺带记录（不在本票范围，但请在 PR 里链出来）：
  GraphIdRegistry.Clear() 是 public static 且连 _frozen 一起复位，这是设计缺陷本身；
  静态注册表的 freeze 合同还不一致（属性表 Clear 在冻结时抛，
  标签表/效果模板表/技能表/图表全部静默解冻）。根治属于 S14 的范围。

禁止：删掉八家族 Mod 本身（那是本票关门之后的独立票）；改动逐节点展厅。

验收：
  Feature: 划掉的门要真的锁上
    Scenario: 退役展厅不再给出可点的入口
      Given 登记表里某个展厅已经标成退役
      When 我在启动器里翻到它
      Then 卡片上不得出现可复制的启动命令
      And 顶栏的预设列表里也翻不到它

  Feature: 进一间展厅不该弄坏别的展厅
    Scenario: mod 启动不再清空引擎的图编号表
      Given 我启动任意一个展厅
      When 该展厅的 mod 完成初始化
      Then 引擎在启动期注册的图编号必须全部仍然有效
```

**该跑：** `python3 scripts/validate-registry.py`；`--filter "FullyQualifiedName~GraphOpsRelShowcaseAcceptanceTests|FullyQualifiedName~GraphOpsQueryShowcaseAcceptanceTests|FullyQualifiedName~GraphOpsAttrShowcaseAcceptanceTests|FullyQualifiedName~GraphOpsSpatialShowcaseAcceptanceTests|FullyQualifiedName~GraphOpsEventShowcaseAcceptanceTests|FullyQualifiedName~GraphOpsNodeGallery"`

---

### S7 · 展厅血条名实相符 + 删掉静默回卷 【P1】

**审查编号：** `pr932_graph_landed_architecture_audit.md` B3

**任务书**

```text
目标：让玩家看到的血条代表它声称代表的东西，并删掉一条藏在代码里的暗规则。

现状（数字来源是真的，落地方式不是）：
  每间展厅先跑真 VM（真 BuiltinHandlerRegistry / EffectTemplateRegistry / EffectRequestQueue 根），
  取出寄存器里的数 —— 这一半是对的，保留。
  但把这个数变成血条那一步是 next -= 图返回值 之后
  AttributeMutationOps.SetBase/SetCurrent 直接写属性，不走任何效果结算；
  而且 LinearNodeDriver 里有 if (next <= 0f) next = opening;
  —— 血要掉到 0 就静默回卷成开局值，而这条规则不在任何分镜数据里。
  EventNodeDriver 也有同类回卷。
  C# 影子数组 ctx.ActorHealth 每拍被 SyncHud 无条件刷回世界。

  重要背景：这套写法之所以「看起来无害」，是因为血量恰好是配了夹上限约束的属性
  （实测：同样写法换到不夹上限的属性上会被聚合器静默丢弃）。
  也就是说现状能用是运气，不是设计。

要做：
  一、删掉静默回卷（这是 NO FALLBACK 红线）
    本该「打死了」的时刻被静默改写成「满血重来」。两条路选一条：
    删掉让它失败关闭，或者把「循环 / 复位」写成分镜里的显式字段（推荐后者，数据驱动）。
    禁止留在 C# 里。

  二、把「演戏」与「真结算」分开处理，不要一刀切
    - 真有结算语义的 op（ApplyEffect* / FanOutApplyEffect* / ModifyAttributeAdd /
      WriteSelfAttribute 等）：走正式效果管线，让血条反映真结算。
      参照 AttrNodeDriver.Tick 那条已经干净的路
      （ExecuteFeaturedGraph + SyncActorHealthFromWorld，C# 只读回来）。
    - 纯算式 op（AddFloat / ClampFloat 之类，图本身不改世界）：血条不是它的正确表达。
      要么改用非血量的指示物，要么在分镜与展厅简介里说清「这根条是示意」。
      今天 showcase.registry.json 的 AddFloat 简介写「血条按总和往下掉」，
      玩家会理解成真结算 —— 文案与实现必须对齐，改哪边都行但必须一致。

  三、ScriptNodeDriver 把「茶水量」写进 ActorHealth，属于血条被当通用数值槽复用，同上处理。

禁止：
  动知识披露与头顶条那一侧（那一侧是对的，别改坏 —— 披露走真 KnowledgeProjectionStore
  与 WorldHud 属性掩码，六角圈人那三间的语义完全干净并有测试逐档断言）；
  改测试门槛来掩盖；把 ctx.ActorHealth 影子数组的读写方向搞反。

验收：
  Feature: 血条要么代表真被打掉的血，要么别装成那样
    Scenario: 打到零不许偷偷回满
      Given 一间展厅里这一刀足以把木桩的血打到零
      When 这一刀结算
      Then 结果必须由数据决定，而不是被代码悄悄改回满血

    Scenario: 展厅简介不能承诺它没做的事
      Given 某间展厅的图只是做纯算术，不改世界
      When 我读它的简介
      Then 简介不得让我以为血条是被真打掉的
```

**该跑：** `--filter "FullyQualifiedName~GraphOpsNodeGallery"`；`python3 scripts/validate-registry.py`

---

### S8 · 假防线集中整治 【P2】

**审查编号：** B7、B26、B27、B28、以及 `pr932` M3/M4 的守卫强度部分

**任务书**

```text
目标：让名字里写着「零分配 / 基准 / 五毫秒」的测试真的在守门。
这一票纯改测试，不动生产代码，但它决定了其他所有修复以后守不守得住。
投入最小，收益最高，建议早做。

真有牙齿的零分配断言目前只有 6 条（AllocationTests 4 条 + MathOpsChain 1 条
+ EntityCollection 1 条）。以下是「测了不断言」或「断言恒真」的清单：

一、测了不断言（只 Console.WriteLine）
  - GraphPerfTests.cs 整个文件零断言。Benchmark_GraphExecutor_SmallProgram
    跑 100 万次图 VM、算出分配字节数、打印、结束。
    这是图 VM 唯一的零分配基准，它只能因为抛异常而失败。
    另：它没有兄弟基准都有的 [Category("benchmark")]。
  - GasBenchmarkTests.cs 8 个 benchmark、5 个测分配，
    全文件只有 2 句关于触发器计数的断言，没有一句关于分配或耗时。
    第 74 行还写着注释「Assert & Log」，实际只有 Log。
  - EffectPhaseStressTests.PhaseExecutor_HighVolume_ReportsThroughputAndGc
    算了 allocDelta、打印、不断言。
  - GraphBehaviorPressureMatrixTests.WritePressureMatrices_M1_M2_M3_M6
    全文件 3 句断言全是 File.Exists —— 只保证 CSV 被写出，不保证数字在任何范围内。
    （附带证据：跑一次测试后 docs/benchmarks/graph-behavior-pressure/matrix-m*.csv
    就会变成 modified，说明这些数字会漂移且无人把关。）
  处理：先跑一遍读 stdout 里的分配数字。如果为零，直接补成断言；
  如果非零，说明存在真实但无人看管的分配 —— 那就是一条新发现，单独开票。

二、断言恒真（比不断言更糟，因为它在盖章）
  - GraphFailFastAndCapacityTests 里 Assert.That(q.DroppedCount, Is.EqualTo(0))：
    该计数器全文没有任何自增，恒为 0。删掉或改成有意义的断言（与 S5 联动）。

三、注释/命名与断言不符
  - EffectPhaseStressTests.PhaseExecutor_HighVolume_...：注释写「<10μs per phase」，
    断言是 Is.LessThan(50.0) —— 差 5 倍。
  - BlackboardOps_Stress_MassWriteRead_ZeroGc：名字带 ZeroGc，
    唯一性能断言是 TotalSeconds < 30.0；更麻烦的是它测不了自己 ——
    CallStack = new int[MaxCallStackDepth] 在被测循环体内，
    每次迭代分配一个新数组，测量区间自己污染自己。先把分配移出循环，再补断言。
  - BehaviorTreeRuntimeTests.ThinkWave_10k_AlwaysSuccess16_UnderFiveMilliseconds：
    断言 Is.LessThan(15.0)，裸断言无消息。
  - FsmRuntimeTests.ThinkWave_10k_SentryHfsmWithScripts_UnderFiveMilliseconds：
    断言 15.0（消息如实写了 15）。
  处理：让名字、注释、断言、失败消息四者一致。
  门槛该放宽就放宽（CI 抖动是真的），但名字必须跟着改。
  参考正面例子：GraphBehaviorArenaAcceptanceTests 的三重门槛
  （over5ms == 0 + avg < 15 + p95 < 15）名实相符且三条都带消息。

四、覆盖缺口
  - 4 条真断言覆盖的是 proposal / 瞬时效果 / 关系事务 / listener 派发，
    完全没覆盖持续效果的 tick 循环：EffectApplicationSystem、AttributeAggregatorSystem、
    EffectLifetimeSystem、TimedTagExpirationSystem、DeferredTriggerCollectionSystem。
    而「每帧两次 archetype 迁移」（见 S9 顺带项 / B9）恰好就发生在这些没被测的系统里。
    至少给持续效果的 tick 补一条零分配断言。

五、顺序守卫其实不守顺序
  - ArchitectureGuardTests.SystemGroup_MustMatchDesignDocument 用
    Assert.That(Enum.GetNames<SystemGroup>(), Is.EquivalentTo(expected)) ——
    EquivalentTo 是集合相等，不比较顺序。把 Cleanup 挪到最前面它照样绿。
    改成按序比较，并且交叉校验 enum 与
    PhaseOrderedCooperativeSimulation.PhaseOrder 两份列表（今天一致但无守卫）。
    另：正式顺序表缺 RuntimeEntityBinding，而它有 9 处生产用法 —— 一并补上。
    还要补一条：某个组在 enum 里有、在 PhaseOrder 里漏了，必须失败
    （今天是注册进去的系统每帧都不跑且无任何诊断）。

六、守卫层自己的债（可以同票做，也可以拆出去）
  - 两个 ArchitectureGuardTests 同名同 namespace、37 个方法逐字节相同的复制，
    合计 144 次 ReadAllText + 6 次反射。
  - 而 gas-order-input-runtime-contract.md 明文写着「不得用 ReadAllText + Contains
    复述源码实现；需要静态门禁时使用编译后的 API、Roslyn/analyzer」——
    守卫层违反了它守的合同。
    至少先去重（留一份）；改成 IL/Roslyn 门禁可以单独开票。

禁止：为了让测试变绿而放宽任何真实门槛；删掉测试而不替换等价覆盖。

验收：
  Feature: 零分配的承诺要有人守着
    Scenario: 名字里写零分配的测试必须断言分配量
      Given 一批名字包含 ZeroAlloc / Benchmark / ZeroGc 的测试
      When 我检查它们的断言
      Then 每一条都必须对分配量下断言
      And 断言的数值必须与方法名和注释一致

    Scenario: 顺序守卫必须真的守顺序
      Given 我把系统阶段的顺序打乱
      When CI 跑架构守卫
      Then 守卫必须失败
```

**该跑：** `--filter "FullyQualifiedName~AllocationTests|FullyQualifiedName~GraphPerfTests|FullyQualifiedName~GasBenchmarkTests|FullyQualifiedName~EffectPhaseStressTests|FullyQualifiedName~EntityCollectionQueryBenchmarkTests|FullyQualifiedName~AiBenchmarkTests|FullyQualifiedName~BehaviorTreeRuntimeTests|FullyQualifiedName~FsmRuntimeTests|FullyQualifiedName~GraphBehaviorArenaAcceptanceTests" --logger "console;verbosity=detailed"`
另跑 `src/Tests/ArchitectureTests/ArchitectureTests.csproj --filter "FullyQualifiedName~ArchitectureGuardTests"`

---

### S9 · L2 宿主走 L1 正式执行前门（引入执行帧） 【P2 · 依赖 S1】

**审查编号：** A5、B15、B16、B17、C13

**任务书**

```text
目标：让所有图执行都经过同一道有校验的门，并让「部分初始化的执行状态」在类型上不可能出现。

现状：
  L1 的正式执行前门 GraphExecutor 每个入口都先 RequireKind 再 RequireAllowed，
  ExecuteScriptSlice 还额外检查六个寄存器 span 的尺寸。
  它在生产代码里零调用者 —— 只有 3 个测试文件用。门造好了没人走。
  四个生产切片宿主全部直接调 GasGraphOpHandlerTable.ExecuteSlice，三项检查全跳过：
    BehaviorTreeWorld / GraphProgramHfsmHost（连 RequireKind 都没有，
    Effect 图挂成状态机条件能跑到 NRE）/ LevelScriptPrograms
  run-to-halt 侧另有 EffectPhaseExecutor / PerformerRuleSystem /
  GraphReturnWriter / AbilityAimPresentationRuntime 也是直连。
  （run-to-halt 的 ExecuteValidation/ExecuteScore/ExecuteDerived 确实有生产调用方，
  那半边门是真的。）

  根因：GraphExecutionState 是 15 字段的 ref struct，没有构造不变式，
  部分初始化合法，全仓 11 个手工构造点完整度各不相同。
  最刺眼的后果：Programs 字段除了测试专用入口与嵌套子帧之外没有任何宿主填写，
  而 HandleInvokeScript 首行 if (s.Programs == null) throw ——
  所有生产执行路径都写得出 InvokeScript，一个都执行不了。
  范围包含 Effect 阶段、Query 输出、Performer 条件、Aim 预览，不止 L2 三个宿主。

要做：
  1) 引入执行帧类型（GraphFrame 或等价）：持有寄存器存储 + cursor + kind + api + programs，
     由 VM 构造并一次性验证。宿主拿帧，不拿裸 ref struct。
  2) 把 GasGraphOpHandlerTable.Execute / ExecuteSlice 收成 VM 内部，
     对外只暴露经过校验的入口。
  3) 七个直连点全部改走新入口。特别注意 GraphProgramHfsmHost 今天连 kind 都不查。
  4) 图号是裸 int，没有 typed id 承载 kind ——
     行为树节点、状态机转移条件、关卡脚本的 graphId 字段建议改成带 kind 的 typed id，
     让「把 Effect 图塞进行为树叶子」在类型层面就不可能。
     如果这一步太大，至少在绑定时校验并失败关闭。
  5) 顺带收口 E[2] 的三种不同预置含义（Viewer / TargetContext / previewTarget）——
     今天它是潜伏的（没有节点能读到预置值，Load* 全读 state 字段），
     但新帧模型应该把它定义清楚。

同票一并修两条相关的执行模型问题：
  - 掉出程序尾部被当成「成功停机」：(uint)pc >= (uint)program.Length → Halted + 返回成功，
    负 pc 也走这条。任何坏跳转都被报成正常完成。
    注释把它写明为「既有 GAS 图掉出尾部算成功完成」—— 一条兼容性 fallback 焊进了 VM 内核。
    改成要求显式终结指令 + 装载期校验跳转目标，pc 越界即错误。
  - GraphExecutionStatus 把「未开始」与「预算耗尽挂起」压在同一个 Running 上，
    而 BehaviorTreeWorld 的续跑判断只认 Yielded：
      bool resumeYield = cursor.Status == GraphExecutionStatus.Yielded;
      if (!resumeYield) { ints.Clear(); bools.Clear(); callStack.Clear(); cursor.Reset(); }
    所以 action 叶子一旦因预算耗尽返回 Running，下一拍寄存器清空、cursor 归零、
    脚本从 pc=0 静默重跑，已产生的副作用重放。这是个真 bug，不是瑕疵。
    把状态区分开（未开始 / 挂起于 Yield / 挂起于预算），并补测试。

顺带（可选，与 B17 相关）：
  程序校验目前发生在每次执行而非登记时。RequireAllowed 是 O(程序长度) 全扫、
  每次执行都跑（登记时已跑过一次）；EffectPhaseExecutor.AnalyzeScratchUsage 是第二遍全扫，
  而它是全系统唯一的寄存器越界校验 —— 其余宿主一概没有，越界只表现为
  span 的 IndexOutOfRangeException 而非失败关闭诊断。
  建议在 GraphProgramRegistry.Register / ReplaceProgram 里跑一次统一的程序校验器
  （kind 能力 + 寄存器边界 + 分支目标 + 终结指令），之后热循环零前置校验。
  这一步会顺手把两遍 O(n) 全扫从热路径拿掉。

禁止：在本票里拓宽 Script 方言（那是 S13，必须在本票之后）；
      为了让 L2 跑通而放宽 kind 校验。

验收：
  Feature: 所有图执行走同一道门
    Scenario: 图的种类不对必须被拒绝
      Given 我把一张效果图绑到行为树的叶子上
      When 代理执行到这个叶子
      Then 必须失败并说明这个种类的图不能挂在这里

    Scenario: 坏跳转不能被报成正常完成
      Given 一张图里有一个跳到程序外面的跳转
      When 这张图被登记
      Then 登记必须失败并指出这个跳转

  Feature: 跨拍的动作要接着跑，不要重头再来
    Scenario: 预算耗尽后从断点继续
      Given 一个行为树叶子里的动作因为当拍预算耗尽而挂起
      When 下一拍继续
      Then 它必须从断点继续，而不是从头重跑
      And 已经产生的效果不得重复发生
```

**该跑：** `--filter "FullyQualifiedName~Ludots.Tests.Gas.AI|FullyQualifiedName~GraphExecuteSliceBudgetTests|FullyQualifiedName~GraphScriptControlFlowTests|FullyQualifiedName~GraphContractTests|FullyQualifiedName~EffectPhaseArchitectureTests|FullyQualifiedName~LifecycleArchitectureTests"`

---

### S10 · 表现层不得决定玩法选中 【P2】

**审查编号：** A6、C22

**任务书**

```text
目标：让「谁能被选中、谁能收指令」只由模拟状态决定，不由相机剔除结果决定。

现状：
  CommandSourcePointerHitResolver 与 CommandSourceAcquisitionSystem 的选中查询签名是
  (Entity, ref VisualTransform, ref CullState, ref CommandSourceSelectableTag)，
  两处都以 if (!cull.IsVisible) return; 开头。
  CommandSourceAcquisitionSystem 由 CoreInputMod 经
  InsertSystemBeforeRequired<AxisMoveOrderSystem>(..., SystemGroup.InputCollection)
  装进模拟循环。
  含义是：相机剔除结果（视觉）决定了哪个实体能被选中，从而决定谁能收到订单。
  同时违反两份合同：
    entity-simulation-layering.md：CullState 只负责视觉，不得作为剔除真相
    gas-order-input-runtime-contract.md：Core 不读取表现层 VisualTransform 作为玩法真相
  全仓的架构守卫里 VisualTransform 出现次数为 0 —— 这个方向零守卫。

要做：
  1) 选中判定改用模拟侧真相：位置用 WorldPositionCm，可选中性用玩法组件
     （CommandSourceSelectableTag 本身是玩法组件，保留），
     可见性若确实需要参与判定，必须用玩法侧的知识/视野投影
     （KnowledgeProjectionStore 已经是正式通道），不是相机剔除。
  2) 补守卫：模拟相（InputCollection / AbilityActivation / EffectProcessing /
     AttributeCalculation 等）的系统不得读 VisualTransform / CullState。
     ArchitectureGuardTests 已有 IL 级调用扫描基建可复用。
  3) 顺带把跨层组件的所有权写清楚（今天没有任何机制表达）：
     PresentationStableId 由模拟侧多处写、表现侧读；
     PresentationDestroyPending 模拟侧多处读写、表现侧消费，连 L0 的
     GasGraphRuntimeApi 都在读它；
     CullState 由 RuntimeEntitySpawnSystem（模拟）创建、CameraCullingSystem（表现）写、
     Input（模拟）读 —— 三方读写、无声明 owner。
     entity-simulation-layering.md 的「单写真相规则」在代码里没有任何执行体。
     本票至少给这三个组件写明 owner 并加守卫；partial-world 隔离属于 S14。

禁止：把剔除标记改名了事；用「反正现在能用」保留视觉 gate。

验收：
  Feature: 镜头看不看得见，不该决定我能不能指挥
    Scenario: 镜头外的单位仍然可以被指挥
      Given 我的一个单位当前不在镜头范围内
      When 我通过编队或框选对它下指令
      Then 指令必须生效
    Scenario: 模拟系统不得读表现层组件
      Given 一个跑在模拟相里的系统
      When CI 跑架构守卫
      Then 若它读了视觉变换或剔除状态，守卫必须失败
```

**该跑：** `src/Tests/ArchitectureTests/ArchitectureTests.csproj --filter "FullyQualifiedName~ArchitectureGuardTests|FullyQualifiedName~Rfc0065InteractionCastingBoundaryContractTests|FullyQualifiedName~PerformContractsAndLegacyLaneTests"`；`--filter "FullyQualifiedName~OrderBufferSystem|FullyQualifiedName~CommandSource"`

---

### S11 · 覆盖表错误归因 + 守卫强度 【P2】

**审查编号：** `pr932_graph_landed_architecture_audit.md` M3、M4

**任务书**

```text
目标：让覆盖表的「已覆盖」是被度量出来的，而不是被结构保证的。

现状：
  覆盖表 120 条，unitTestFilter 有 21 条错误归因 —— 登记的画廊测试根本不执行那个 op。
  最典型：事件族 15 个 op 全指向 SnapToNearestInCollection_SucceedsWithPlayerCaption
  （一个单 op 测试），而 SendEvent_BroadcastsPlayerReadableHit、
  ClampTargetToRange_PullsLandingPointInRange、LoadViewer_ReadsTheAudience、
  KnowledgeHasProjection_ShowsVisible、SnapToNearestGraphEdge_SnapsOntoTheRoad
  就在同一个类里却零引用；
  ConstFloat / AddFloat 指向 FloatFamilyOp_RendersPlayerCaption，
  而那个 TestCaseSource 数组里没有这两个 op。

  守卫抓不到的原因（这是根因，比表面数字更重要）：
    1) LoadGasTestMethodNames 只按方法名建集合、丢掉类名，
       校验时只问「全 GasTests 里有没有这个方法名」。
    2) hasGalleryTest 只检查前缀是否 GraphOpsNodeGallery，
       而那两条 token 是生成器无条件注入的 —— 生成器写什么守卫就查什么，闭环自证，
       这条断言永远不可能失败。
    3) 生成器的 DRIVER_FAMILY_TEST 按 driver 字段盲配，
       不校验该 op 是否在被配测试的 op 列表里 —— 这是 21 条错误归因的产生机制。
    4) status 只剩 covered 一个合法值（生成器拒绝非 covered，守卫也把非 covered 记为 failure），
       P2 计划里的 missing / runtime-only 已无法表达。

要做：
  1) 守卫改成 (类, 方法) 对，而不是方法名集合。
  2) 更重要：守卫要能校验「被引用的测试真的执行该 op」——
     需要能展开 [TestCaseSource] / [TestCase] / 方法内 op 数组字面量。
  3) 生成器改成：只有该 op 真在目标测试的 op 列表里才写进 filter；
     否则指向它的 op 专属测试（很多已经存在，只是没被引用）；
     都没有就让生成器失败关闭。
  4) hasGalleryTest 改成要求至少一条 op 专属画廊测试。
  5) 把 ExistingVignettes_CompileWithFeaturedOp（唯一断言编译产物真的
     emit 了那个 opcode 的测试）与 GeneratedMaps_SpawnEveryVignetteActor 纳入 filter，
     今天它们引用次数是 0。
  6) unitTestFilter 字段名与内容不符：它是分号拼接的裸 Class.Method，
     不是可运行的 filter 表达式，生成器还主动剥掉 FullyQualifiedName~ 前缀。
     要么改成真可运行的表达式，要么改字段名。
  7) 补一条 CI 闸门：跑生成器并比对是否有漂移。
     今天 120/120 对齐是人工正确，不是机制保证 ——
     .github/workflows/ 全目录没有该脚本的引用。
     （已实测：当前复跑零漂移，所以这条闸门加上去就是绿的。）
  8) 生成器只 upsert 不删除：退役或删掉一个节点会留下全套孤儿门
     （binding / preset / registry 条目 / 薄入口 Mod / 画廊地图），
     而 validate-registry.py 仍判通过（孤儿两边都在、双向一致）。
     这正是八条家族 binding 残留的机制（见 S6）。加删除或加孤儿检测。

禁止：为了让守卫变绿而把那 21 条改成 status=missing 了事 ——
      那 21 个 op 大多有真测试，问题是指针指错了；
      先修指针，确认真的有 op 没有专属测试再谈降级。

验收：
  Feature: 说覆盖了就要真的覆盖
    Scenario: 登记的测试必须真的跑那个节点
      Given 覆盖表给某个图节点登记了一条测试
      When 守卫检查这条登记
      Then 那条测试必须真的执行这个节点
    Scenario: 生成器的产物不能被手改而无人知
      Given 有人手改了生成器产出的文件
      When CI 复跑生成器并比对
      Then 必须失败并指出漂移的文件
```

**该跑：** `--filter "FullyQualifiedName~GraphNodeOpCoverageRegistryTests|FullyQualifiedName~GraphOpsNodeGallery"`；`python3 scripts/generate-graph-op-node-galleries.py --strict` 后 `git diff --exit-code`

---

### S12 · 寄存器归属与指令 descriptor 【P2 · 依赖 S9】

**审查编号：** B14、B18、C12

**任务书**

```text
目标：让寄存器索引有单一归属，让 kind × opcode 能力矩阵有单一数据源。

一、寄存器（B14）
  今天有三套互不相认的占位机制：
    1) bump 分配器：AllocateOutputs 里 intNext++ / boolNext++ / floatNext++，
       无 liveness 复用（约束是「产值节点数 ≤ 32」而非「同时活跃值 ≤ 32」）。
       溢出是 RegisterOutOfRange 编译错 —— 这点是对的，保留。
    2) 作者手填 PinRegister：只对 int 生效，且只在 intNext <= pin 时抬高 intNext。
       pin 小于当前 intNext 时没有任何诊断 —— 静默与已分配寄存器别名。
    3) 硬编码常量 scratch：TargetListGet 与 SnapToNearestInCollection 的 validOutput
       都写死 B[31]（MaxBoolRegisters - 1）。
  而唯一做对的分配器 AllocateSugarScratches 是按 used-set 找空位的，
  但它的 used-set 只包含 outputRegisters，不包含 B[31] ——
  bool 用满 31 个时它会把糖的 scratch 分到 31，第三次相撞。
  再叠上宿主侧的 ABI 槽：B[0] = Validation 判定、F[0] = Score 分数、
  I[0] = 行为树 sensor、B[31] = scratch。这四个槽的 SSOT 不在代码里任何一处
  （B[0] 写在 placement-validation-ssot.md，F[0] 写在 graph-funclib-actionlib-contract.md），
  而 int/bool/float 分配器都从 0 开始。只有 entity 有真 ABI 并被分配器遵守
  （E0 caster / E1 target / E2 viewer，entityNext = 3）。

  要做：一个 GraphRegisterFile 类型，持有「按 kind 声明的保留槽 + used-set + 分配 + AllocScratch()」。
  GraphVmLimits 只提供容量。任何 scratch 必须走 AllocScratch()，
  常量索引在编译器里被禁止出现。PinRegister 别名必须报诊断。
  liveness 复用可以后置 —— 它是容量问题，前面这些是正确性问题。
  顺带：上限 32 不是瓶颈（32 个 float 才 128 字节），真正的天花板是
  GraphInstruction 的 Dst/A/B/C 是 byte（最多 255），所以 32 → 64 几乎零成本，
  但先别顺手改容量，先把归属收干净。

二、能力矩阵（B18）
  前门是三张手写 op 列表（线性 86 / 查询 41 / Script 11），
  一张线性表被 4 个 kind 共用，而这 4 个 kind 有 3 套不同策略。
  缝隙已量化：Score / Validation / Derived 各有 19–20 个「前门允许但策略拒」
  （策略在装载期赢，所以是失败关闭，不是漏洞 ——
  但作者拿到的诊断是「操作不被 kind 允许」而不是「这个节点在这个方言里不存在」）；
  各 kind 另有 33–89 个「策略允许但前门写不出」。
  GraphKindOperationPolicy 半数据驱动，外加 3 处硬编码例外
  （Yield 只给 Script、Derived 额外放 WriteSelfAttribute、listener 的三个 LoadConfig*）。
  没有任何测试断言「前门 ⊆ 策略」。

  要做：一张 per-op descriptor 表（kinds × ports × operand roles）作为唯一数据源，
  前门矩阵、GetLinearOutputType / GetQueryOutputType、端口白名单、
  覆盖表的 authorableKinds 全部由它投影。
  GraphKindOperationPolicy 的 3 处例外变成 descriptor 上的字段。
  补一条「前门 ⊆ 策略」的一致性断言。

三、指令编码（C12，同批做成本最低）
  GraphInstruction 只有 Op/Dst/A/B/C/Flags/Imm/ImmF，
  而 GraphInstructionFlags 只定义了 1 个 bit。实际 Flags 承担至少 7 种含义
  （bool 寄存器索引、空间容量策略、relationship typeId、排序 descending、
  teamId 来源选择、EffectArgs float 个数、FuncLib 标记），
  Dst 承担 4 种（目标寄存器 / relationship typeId / reasonId / dispatch preset id）。
  这套语义必须在三处一致：编译器 emit、符号 patch、handler。
  descriptor 表落地后，operand role 应当由它声明。
  同时修一个真问题：GraphProgramSymbolPatcher.Patch 不是幂等的 ——
  它把符号索引原地覆写成解析后的 id，第二次跑会把 id 当符号索引再解析
  （静默错绑或越界抛）。热改路径值得单独查一遍。

禁止：在本票里改容量上限；发明新的 opcode。

验收：
  Feature: 两个节点不会抢同一个格子
    Scenario: 所有临时格子都由分配器发放
      Given 一张图同时用到了目标列表读取和吸附的有效位
      When 它被编译
      Then 这两个输出不得落在同一个格子上
    Scenario: 作者钉的格子与已分配的冲突时要报错
      Given 我把某个节点的输出钉到一个已经被分配出去的格子
      When 这张图被编译
      Then 编译必须失败并指出冲突

  Feature: 文档说能写的节点，作者真写得出
    Scenario: 前门与能力策略不得互相矛盾
      Given 任意一个图种类
      When 我列出前门允许写的节点，与能力策略允许执行的节点
      Then 前者必须是后者的子集
```

**该跑：** `--filter "FullyQualifiedName~GraphControlFlowConfigCoverageTests|FullyQualifiedName~GraphContractTests|FullyQualifiedName~GraphEffectAuthoringExpressivenessTests|FullyQualifiedName~GraphQueryControlFlowTests|FullyQualifiedName~GraphNodeOpCoverageRegistryTests|FullyQualifiedName~LiveGasEditPipelineTests"`

---

### S13 · Script 方言拓宽 + L2 作者面 【P2 · 依赖 S9 + S12】

**审查编号：** B19、B20

**任务书**

```text
目标：让「一切皆 Mod」在 L2 上也成立。

先读这条顺序依赖，它是硬的：
  Script 方言今天只有 11 个可写 op（int 数学 + 控制流），
  读不到属性、读不到黑板、做不了 float 运算、发不了查询。
  于是行为树必须用 C# 的 IBehaviorTreeSensorFeed 把感知结果塞进 I[0]。
  而三个 L2 宿主之所以能只填 I / B / CallStack 就跑，正是因为 Script 这么窄 ——
  往 Script 矩阵里加任何一个 LoadAttribute / ReadBlackboard* / Query* 类的 op，
  行为树 / 状态机 / 关卡立刻崩：F / E / Targets 是零长 span，Api 与 Programs 是 null。
  所以必须先做 S9（宿主拿到完整执行帧），再做本票。反了必崩。

要做：
  1) 拓宽 Script 方言的能力矩阵（经 S12 的 descriptor 表），
     让 L2 叶子能自己读属性、读黑板、做查询。
  2) 给 L2 一个数据作者面。今天 AI 配置覆盖 Utility / GOAP / HTN / atoms /
     projections / bindings，但没有行为树、没有状态机 ——
     拓扑只能在 C# 里用 BehaviorTreeFactory / HfsmFactory 造。
     需要 JSON schema + loader，让 mod 能定义自己的树与状态机。
  3) 把住在 Core 的玩法语义搬出去：
     HfsmFactory.CreateSentryHierarchy(WithScripts) 的完整状态机拓扑
     （Idle → Alert → Combat → Retreat）、
     BehaviorTreeFactory.CreatePatrolChaseAttackTree 的整棵巡逻-追击-攻击树、
     以及 BehaviorTreeScriptKeys / HfsmScriptKeys / LevelScriptKeys 里的 11 个名字。
     这 11 个名字同时又是 action_lib.json 的全部条目名 —— 同一份内容两个 SSOT，
     搬出去的同时把 SSOT 收成一份。
  4) L2 叶子调纯算式清单：三个宿主构造执行状态时都不填 Programs，
     而 InvokeScript 首行就检查它 —— 所以 ActionLib 动作调 FuncLib 纯函数必抛。
     合同 §3.3 把 FuncLib 的调用方明确列为「…Script、ActionLib」。
     S9 的帧模型应该已经解决了这个，本票确认它真的能用并补测试。
  5) 合同 §4.4 写「HFSM OnTick → ActionLib『警戒一步』（内含 Yield）」，
     实现相反：GraphProgramHfsmHost.RunAction 非 Halted 即抛
     「Yield is not allowed on lifecycle bindings」，Level RunScript 同样。
     且 GraphActionCatalogLoader 不按宿主区分 Yield 策略 ——
     含 Yield 的 hfsm.* 条目能加载通过、只在运行时炸，加载期没有失败关闭。
     裁决「HFSM 到底能不能挂含 Yield 的动作」，然后让合同与实现一致，
     并把宿主维度的 Yield 策略校验挪到加载期。

禁止：在 S9 之前开工；用「给 Core 工厂加参数」代替数据作者面。

验收：
  Feature: 行为可以用数据写，不必改引擎
    Scenario: Mod 定义自己的行为树
      Given 我在 mod 里用配置写了一棵巡逻-追击-攻击的树
      When 游戏加载这个 mod
      Then 代理应当按我写的树行动
      And 我不需要改 Core 的任何 C# 代码

    Scenario: 叶子自己能感知
      Given 一个行为树叶子需要读取目标的血量来决定是否撤退
      When 它执行
      Then 它应当能直接读到血量
      And 不需要 C# 侧先把结果喂进来
```

**该跑：** `--filter "FullyQualifiedName~Ludots.Tests.Gas.AI|FullyQualifiedName~GraphContractTests|FullyQualifiedName~GraphActionCatalogLoaderTests|FullyQualifiedName~ScriptFlowSandboxShowcaseAcceptanceTests|FullyQualifiedName~GraphBehaviorSeparatedShowcaseAcceptanceTests"`

---

### S14 · 分层物理化（拆程序集） 【P3 · 需先出设计】

**审查编号：** B21、B22、B23、B24、B25、C21、C22

**任务书**

```text
目标：让层与层之间的墙在编译期存在，而不是靠运行期检查与文本扫描事后追认。
这是唯一的大工程，也是其他所有边界问题的根因。**先出设计，评审通过再动手。**

现状：
  src/Core/Ludots.Core.csproj 一个程序集同时装下
  L0 VM、L1 编译器、L2 调度器、Presentation、Input、Spatial、Navigation
  （只有 Physics2D 三个项目被拆出去）。
  所以：
    - L0 的数据半边（src/Core/GraphRuntime/）有一条守卫说它不许引用 GAS，
      但这条守卫已被 GraphControlFlowDocument.cs 的传递依赖穿透；
      而 L0 的执行半边（GasGraphOpHandlerTable）根本不在守卫目录里，
      它的 using 段直接写着 GAS / Placement / Relationships / Teams。
      L1 编译器还依赖 Presentation.TagDisplay。
    - Core/Mod 端口被一跳绕开：IModContext 设计干净，
      但 ScriptContext.GetEngine() 交出具体 GameEngine，mods 里 205 处在用；
      另有 100 处直接 RegisterSystem 进 6 个组，无白名单无能力声明。
      Mod 还直写 Core 进程级静态：TeamManager(7 mod)、
      TagRegistry / AttributeRegistry(4)、GraphIdRegistry.Clear(5)、
      SetCoordinateConverter(3)。
    - 静态注册表 + 实例级注册表并存：GraphIdRegistry（static，name→id）
      与 GraphProgramRegistry（实例级，id→program）。
      GraphIdRegistry.Clear() 连 _frozen 一起复位、且是 public static。
      同进程二次装载时 id 错位是必然而非偶发。
      同族的 freeze 合同还不一致：属性表 Clear 在冻结时抛，
      标签表 / 效果模板表 / 技能表 / 图表全部静默解冻。
    - 跨层组件所有权无机制表达（见 S10 第 3 条）。
    - 守卫层自己违反它守的合同：两个 ArchitectureGuardTests 逐字节复制，
      合计 144 次 ReadAllText，而合同明令要用编译后 API / Roslyn / analyzer。

设计要回答的问题（这是本票第一阶段的交付物，不是代码）：
  1) 切几个程序集、边界画在哪。至少要能表达：
     L0（VM + 指令 + 注册表接口）、L1（编译器 + 前门）、L2（行为调度）、
     GAS（效果 + 属性 + 标签）、Presentation、Input、Spatial。
     哪些是 abstractions-only 的契约程序集？
  2) 注册表怎么从进程级静态变成实例状态
     （挂在引擎上，或一个交给各 loader 的 ModRegistrySet）。
     Clear() 消失、换新实例；Freeze 是该实例上的单向转换。
     这是「多地图 / 多引擎实例 / 热改」三件事共同的前置。
  3) Mod 只能看见什么。IModContext 要不要成为唯一通路、GetEngine() 怎么退役、
     205 个调用点怎么分批迁移。
  4) 跨层组件（PresentationStableId / PresentationDestroyPending / CullState /
     VisualTransform）的所有权怎么在类型或 world 层面表达。
  5) SystemGroup 顺序的单一数据源（今天 enum 与 PhaseOrder 两份、无交叉校验、
     漏组静默失效；顺序守卫用集合比较。S8 会先做临时守卫，本票做根治）。
  6) 迁移路径：怎么分批、每批怎么保证不回退、CI 怎么守。

禁止：不出设计直接开始搬文件；一次性大爆改。

验收（第一阶段只验设计）：
  Feature: 层与层之间的墙在编译期就在
    Scenario: 设计能回答边界问题
      Given 一份分层物理化设计
      When 维护者评审
      Then 它必须回答上面六个问题
      And 给出可分批执行、每批可验证的迁移路径
```

---

### S15 · 验收页 SSOT 合并（链接部分已在本计划 PR 修掉） 【chore】

**审查编号：** `pr932_graph_landed_architecture_audit.md` M13（已关）、M12（仍开）

**已完成部分**：`gitbook/acceptance/graph-funclib-actionlib-uat.md` 的相对链接少一级 `../`，
导致 `missing-link-target` 规则在 `main` 上持续报红、挡住所有改文档的 PR。
本计划所在的 PR 已修掉这一行，`scripts/validate-docs.ps1` 现在全仓通过。
**剩余部分**是同一个文件的 SSOT 合并，见下。

**任务书**

```text
目标：把验收页合成一份，消除自相矛盾的覆盖结论。

背景：链接那一行已经修好了（docs-governance 现在是绿的），本票只剩文档合并。

现状（见 pr932 报告 M12）：
  这个文件里有两个 H1，是两份文档被拼接，对同一批合同 §6 场景给出互相矛盾的结论
  （一处说已覆盖、一处标 Gap）。合成一份，逐条以实测为准：
    - bt.patrol 的跨拍续跑已覆盖，下半那句「缺一条 bt.patrolStep 真 Yield」已不成立。
    - 「技能阶段不能调用 ActionLib」：合同原文用 InvokeAction，该节点全仓不存在，
      真正存在的守卫是 FrontDoor_EffectInvokeScriptFunctionNameActionLibName_PatchFailsClosed，
      且同一红线在查询方言上还是开的（见 S3）。照实写，不要写成已覆盖。
    - 合同 §6 写 bt.patrolStep，资产实际叫 bt.patrol，名字要对齐（一处为准）。
  gitbook/SUMMARY.md 指向的是被删掉的下半标题，同步更新。

验证：pwsh ./scripts/validate-docs.ps1 必须仍然零 finding（合并时别把链接改回去）。

禁止：删场景来让映射表好看；把「等价守卫存在」写成「合同原文场景已覆盖」。
```

---

## 4. 场景（这些修完之后，玩家与作者会看到什么变化）

1. **技能作者写错图不再让所有人的游戏消失**，而是在加载时拿到一条「这里有个调用环」的报错。
2. **策划写进属性的数字不再自己变回去**；写错属性名会立刻报错而不是"这个效果没反应"。
3. **技能结算里不会混进会等一拍的动作**，不管作者从哪个方言写。
4. **高压帧不再静默丢联动**：血量触发被动该响就响，容量到顶会明确报错让人去调容量。
5. **一次命中要么全算要么全不算**，不会留下一半回滚的世界。
6. **启动器里划掉的门是真锁上的**，点不进去，也不会因为点进去而弄坏别的展厅。
7. **展厅的血条代表它声称代表的东西**，纯算式的展厅不再假装在打伤害。
8. **镜头看不见的单位仍然可以指挥。**
9. **行为树和状态机可以用数据写**（S13 之后），mod 作者不必改引擎 C#。

---

## 5. 边界

**本计划包含**：上述十五个子任务的目标、现状定位、要求、禁止项、验收与该跑的测试。

**不包含**：
- UI 面板债（#886 / #893）、表现层改名、#723 评分预算
- 删除八个家族 Mod（S6 关门之后的独立票）
- 图 VM 执行核心、编译器前端、组件 SoA 布局的重写——审查结论是这些**是对的**
- 把 opcode 数量当问题——120 个不是问题，分派表撑得住再加 100 个

**修复时请勿顺手改坏的东西**（审查确认它们是对的）：
- VM 的类型初始化完备性硬闸（缺 handler / 描述 / 分类就抛）
- 执行状态的 `ref struct` + `Span` 寄存器设计
- 编译器的值边类型检查、未定义寄存器读检查、不可达检测、编译期指令预算
- `AddFixed<T>` 的「预分配 + 容量闸门 + 超限抛」模式
- 派生属性写入的围栏（scope 独占 + 暂存 + commit-or-discard + 越权即抛）
- 缺 `DirtyFlags` 的失败关闭设计（`.WithNone<DirtyFlags>()` 配只抛异常的 job）
- 技能→效果→图 的单向性（由闭合枚举在类型层面兜住）
- 知识披露与头顶条那一侧（六角圈人三间的语义完全干净且有逐档断言）

---

## 6. UAT（Epic 关单条件）

```gherkin
Feature: 引擎不会因为一份配置而崩掉或说谎
  作为维护者
  我希望这套战斗与图能力的地基是可信的
  以便建立在它上面的玩法不需要反复怀疑引擎

  Scenario: 三条 P0 全部关闭
    Given 一份会自己调自己的图、一个不夹上限的属性、一张从查询里调动作的图
    When 我把它们分别喂给引擎
    Then 第一个必须在登记时被拒绝，且进程不得退出
    And 第二个写进去的数值语义必须唯一且与属性约束无关
    And 第三个必须在编译时被拒绝

  Scenario: 失败路径本身可靠
    Given 任意一次结算在中途失败
    When 回滚发生
    Then 回滚必须完整走完且自身不抛
    And 不得留下一半生效的世界

  Scenario: 容量与遥测不再说谎
    Given 任意一个固定容量的队列被打满
    When 溢出发生
    Then 系统必须抛出并点名容量与来源
    And 仓库里不得存在「有读取接口但从不被写入」的遥测字段

  Scenario: 防线是真的
    Given 所有名字里包含零分配、基准、耗时门槛的测试
    When 我检查它们
    Then 每一条都必须对它声称的指标下断言
    And 断言数值必须与方法名和注释一致

  Scenario: 玩家门名实相符
    Given 启动器里的展厅列表
    When 我逐一点开
    Then 划掉的门必须点不进去
    And 点得进去的门，字幕与血条必须与它的简介一致
```

---

## 7. 相关文档

- 审查结论（本计划的依据）：[GAS + Graph VM 架构审查](gas_graph_architecture_review.md)
- 前序：[PR932 main 图能力收口架构审计](pr932_graph_landed_architecture_audit.md)
- 前序：[PR911 审计修复清单](pr911_audit_fix_checklist.md)
- 判据：[GAS 分层架构](../../gitbook/architecture/gas-layered-architecture.md)
- 判据：[图分层、流程与行为](../../gitbook/architecture/graph-layering-flow-and-behavior.md)
- 判据：[图复用库合同](../../gitbook/architecture/graph-funclib-actionlib-contract.md)
- 判据：[实体模拟分层](../../gitbook/architecture/entity-simulation-layering.md)
- 判据：[Mod 架构](../../gitbook/architecture/mod-architecture.md)
- 判据：[GAS、订单与输入运行时合同](../../gitbook/architecture/gas-order-input-runtime-contract.md)
