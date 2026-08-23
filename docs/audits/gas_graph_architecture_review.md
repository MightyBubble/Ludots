# GAS + Graph VM 架构审查（当时结论）

**当时对象：** `origin/main` @ `82ddb3322a` 的 GAS 与图 VM 全栈
**现在怎样：** [图能力唯一入口](../../gitbook/architecture/graph-capability-status.md)
**范围：** `src/Core/Gameplay/GAS/`（215 个文件）、`src/Core/NodeLibraries/`（28 个）、及其牵连的 `src/Core/GraphRuntime/`、`Spatial/`、`Association/`
**判据：** `gitbook/architecture/gas-layered-architecture.md`、`gitbook/architecture/graph-layering-flow-and-behavior.md`、`gitbook/architecture/graph-funclib-actionlib-contract.md`、`gitbook/architecture/entity-simulation-layering.md`、`gitbook/architecture/mod-architecture.md`、`gitbook/architecture/gas-order-input-runtime-contract.md`
**配套修复分配：** [GAS + Graph VM 架构修复计划](gas_graph_architecture_fix_plan.md)

本文件是**当时那一轮**审查的结论，不是当前进度。

**事后更正（主干 `d1b8f5f4d7`）：** 当时写「启动器里八张已退役灰标卡还能复制启动命令」。后来锁门并删除这些房间。自己调自己杀进程、属性写了又变回去、查询口偷挂动作，后续 S 票已关。分层只合了脚手架，墙没砌完。和现状页打架，以现状页为准。

---

## 1. 概述

### 一句话结论

**骨架比收尾好得多。** 执行核心、组件布局、零分配纪律都是认真做的，不需要重做；风险几乎全部集中在三类"收尾"上——跨图与跨层的传播分析缺失、失败路径本身不可靠、以及一批看起来在守门实际不断言的防线。

### 分层评级

| 层 | 评级 | 一句话 |
|----|------|--------|
| 图 VM 执行核心（L0） | **好** | 纪律写进了运行期契约，不是文档口号；能再承载 100 个 opcode |
| 图编译器与作者前门（L1 编译期） | **良** | 有真前端（类型检查、未定义寄存器读检查、不可达检测），单一入口；寄存器索引归属未收口 |
| 能力模型（kind × opcode） | **有结构缺陷** | 逐指令、只看本图；调用被当控制流而非能力传递 |
| 效果管线事务性 | **设计到位、收尾有洞** | 骨架专业，但回滚不是不可失败的，边界比一次结算小一圈 |
| 属性系统 | **地基有裂缝** | 数值权威随运行时状态翻转，且无任何强制手段 |
| 分层与边界 | **一堵真墙 + 几堵纸墙** | 只有「技能不能直接跑图」由类型系统兜住 |
| ECS 纪律（实现） | **良** | 零 LINQ、零热路径字符串、零组件引用字段；1 处闭包 |
| ECS 纪律（防线） | **差** | 真有牙齿的零分配断言只有 6 条 |

### 三条必须优先处理

1. **图调用无界递归会杀进程**（唯一"今天就能炸"）
2. **属性数值的权威会随运行时状态翻转**（地基）
3. **查询方言可从纯管道调起可挂起动作**（合同红线，作者今天就能写）

---

## 2. 结构

```text
方向 A  图 VM 核心：寄存器模型 / 分派 / 执行模型 / 能力矩阵 / 静态注册表
方向 B  效果管线：阶段序 / Pre-Main-Post / listener / 事务性 / 冻结 / 容量
方向 C  属性系统：写入路径 / base-current-modifier / dirty / id 约定
方向 D  分层边界：L0-L1-L2 / Core-Mod / SystemGroup / 平行体系 / 模拟-表现
方向 E  ECS 纪律：分配 / 查询形态 / 结构变更 / SoA / 容量 / 性能防线
方向 F  主审交叉实测：把 A–E 的推导性断言实跑证实或推翻
```

方向 F 推翻了零条、证实了四条关键推导（§6）。

---

## 3. 详情

### 3.1 立得住的部分（不要动）

| 结论 | 证据 |
|------|------|
| 只有一台 VM、一个解释循环。`Execute`（跑到停）与 `ExecuteSlice`（跨拍续跑）共用 `ExecuteSliceCore`，前者只是「固定预算 + 拒绝 Yield + 预算耗尽抛」的薄壳 | `src/Core/NodeLibraries/GASGraph/GasGraphOpHandlerTable.cs` |
| 分派是类型初始化时预建的 512 槽委托数组，热路径一次索引一次间接调用，零分配零查表 | 同上；`GraphVmLimits.HandlerTableSize` |
| **类型初始化期的完备性硬闸**：每个枚举值必须有 handler + 描述 + 操作分类，缺一样第一次碰到 `Instance` 就抛；`Register` 拒重复、拒空描述 | `GasGraphOpHandlerTable.cs`（`EnsureRegistrationComplete`、`CreateOperationMetadata`） |
| 执行状态是 `ref struct`，寄存器全是 `Span`，由调用方 `stackalloc` 提供；调用栈不够深就抛，错误话术写明「这条路禁止堆分配」 | `GraphExecutionState.cs`；`GasGraphOpHandlerTable.Execute` |
| 编译器有真前端：值边类型检查、未定义寄存器读检查、必需控制边、不可达检测、指令预算编译期检查 | `GraphControlFlowCompiler.cs` |
| 寄存器由编译器分配而非作者手填；溢出是编译期 `RegisterOutOfRange` 失败关闭 | `GraphControlFlowCompiler.AllocateOutputs` / `Alloc` |
| 组件全是定长 `unsafe struct` + `fixed` 数组，且组件内部再拆成并行数组（`ActiveEffectContainer` 把 Entity 拆成 Ids/WorldIds/Versions 三条）；`Components/` 目录搜引用类型字段零命中 | `src/Core/Gameplay/GAS/Components/` |
| 零 LINQ（连 `using System.Linq` 都没有）、零 tick 内字符串拼接、热路径仅 1 处闭包；inline query 与 chunk 迭代 23 处 vs lambda 查询 1 处 | 全目录扫描 |
| `AddFixed<T>` 模式：预分配 `List<T>` + 显式容量闸门，超限抛 `FixedListCapacityExceeded`，既零分配又失败关闭 | `EffectApplicationSystem.cs`、`EffectLifetimeSystem.cs` |
| 效果管线的合同主体成立：阶段序、Pre→Main→Post、模板 Main 优先、`main` 与 `skipMain` 冲突拒绝、四执行窗口全成功才一次性冻结、listener 与阶段图同批认证、`OnPropose` 未写结果按拒绝、listener 禁 `InvokeBuiltin`/`LoadConfig*`、禁重复注册、禁保留 ID 0、未认证能力（`RevealArea`）拒作阶段图 | `EffectPhaseExecutor`、`EffectExecutionPlanCompiler`、`EffectTemplateRegistry`、`EffectPhaseListenerContract`、`BuiltinHandlers` |
| 技能→效果→图 单向，且由**类型系统**强制：`ExecItemKind` 闭合枚举无 Graph 条目，`GraphNodeOp` 无技能激活 opcode；技能跑图只跑 Validation 且双重闸门 | `AbilityExecComponents.cs`、`AbilityActivationPreconditionEvaluator.cs` |
| 派生属性写入是**真围栏**：scope 独占、只允许写自己、scope 内一切副作用 op 直接抛、暂存 buffer commit-or-discard | `GasGraphRuntimeApi`（`BeginDerivedAttributeWrites` / `EndDerivedAttributeWrites` / `RejectDerivedAttributeSideEffect`） |
| 缺 `DirtyFlags` 是**结构上不可能漏**的失败关闭：aggregator 专门开一个 `.WithNone<DirtyFlags>()` 查询配一个只会抛异常的 job | `AttributeAggregatorSystem` |

### 3.2 阻断

| # | 问题 | 证据 | 实测 |
|---|------|------|------|
| **A1** | **`InvokeScript` 嵌套无深度上限、步数预算不复合、装载期不拒环 → 一行配置杀进程。** 深度上限 16 只约束程序内 `Call`；4096 步预算每层递归重新计数，实际上限是 4096^深度；唯一遍历跨图调用边的校验器遇到环时是 `return false`（当作「没找到 Yield」放行）而非判错 | `GasGraphOpHandlerTable.HandleInvokeScript`（递归调 `Execute`，每层 `stackalloc` 约 3.8KB）；`GraphYieldPurityValidator`（`activeGraphs.Add` 失败即 return false）；`GraphVmLimits` | **是**。注册自递归 Script 图，装载期全绿，运行期递归 1495 层栈溢出，进程退出码 134，`try/catch` 无法进入 |
| **A2** | **属性数值的权威随运行时状态翻转。** 同一个正式 API 写入：受 `ClampCurrentToBase` 保护的属性上存活；无约束且有活跃聚合 modifier 的属性上被静默丢弃。且 `SetCurrent` 不排 `AttributeAggregateDirty`，覆盖时机与写入完全脱钩——值可存活任意多帧，直到不相干事件把实体标脏才被抹掉 | `AttributeAggregatorSystem.RestorePersistentCurrentValues`：`if (!touchedByAggregation \|\| clampsToEffectiveCap) SetCurrent(i, previous)` | **是**。三格对照见 §6 |
| **A3** | **写属性的路是并行而非分层，且无任何强制手段。** `AttributeBuffer` 写入面全 `public`，`AttributeMutationOps` 是 `public static`，无 `internal` 收口、无 analyzer 工程、无守卫测试。真结算与直接摆数字在世界状态、dirty flag、presentation bits 上留下**逐字节相同**的痕迹，无 provenance 字段。Core 自己 7 处裸写，mod 10+ 处，且有一条守卫测试反过来把绕行钉成契约 | `AttributeBuffer.cs`；`AttributeMutationOps.cs`；`ArchitectureGuardTests.Issue250_...`（`Does.Contain("attributes.SetCurrent")`） | — |
| **A4** | **查询方言允许 `InvokeScript.graphId`，作者可从 L1 纯管道调起可挂起动作。** 线性方言明确拒绝裸图号，查询方言只拒「两个都给」和「两个都不给」；`action_lib.json` 11 条里 9 条不含 Yield，全部可被一张查询图调起。违反合同 §3.4 明文禁令，零测试覆盖 | `GraphControlFlowCompiler.Query.cs` 对照 `.Linear.cs` | — |
| **A5** | **L2 三个宿主全部绕过 L1 正式执行前门直连 VM**，不做 `RequireKind` / `RequireAllowed` / 寄存器尺寸检查。而写得很规矩的 `GraphExecutor.ExecuteScriptSlice` 在生产代码里**零调用者**（只有 3 个测试文件用）。图号是裸 `int`，没有 typed id 承载 kind，任何已注册图都能塞进行为树叶子 / 状态机转移条件 / 关卡脚本 | `BehaviorTreeWorld.cs`、`GraphProgramHfsmHost.cs`、`LevelScriptPrograms.cs` 对照 `GraphExecutor.cs` | — |
| **A6** | **表现层可见性 gate 玩法选中。** 选中查询以 `if (!cull.IsVisible) return;` 开头，且读 `VisualTransform`——相机剔除结果决定谁能被选中、从而决定谁能收指令。同时违反 `entity-simulation-layering.md`「`CullState` 只负责视觉，不得作为剔除真相」与 `gas-order-input-runtime-contract.md`「Core 不读取表现层 `VisualTransform` 作为玩法真相」。全仓守卫里 `VisualTransform` 出现 0 次 | `CommandSourceAcquisitionSystem.cs`、`CommandSourcePointerHitResolver.cs` | — |

### 3.3 Major

| # | 问题 | 证据 |
|---|------|------|
| **B1** | **事务回滚自身可抛 → 半回滚。** 同一方法内相邻循环风格不一致：恢复效果容器与效果组件的循环有 `IsAlive && Has` 守卫，紧接着三个黑板循环与取消标记循环是裸取组件。阶段执行期间若有实体被销毁，回滚中途抛出，其后的 listener buffer / relation / 结构性移除全不执行。真事务的铁律是回滚路径必须无条件成功 | `EffectPhaseSideEffectTransaction.RollbackWorldWrites` |
| **B2** | 提交路径内 `World.Destroy` 不可逆，其后任一异常留下「实体没了但属性/标签被回滚」的混合态 | `EffectPhaseSideEffectTransaction.Commit` |
| **B3** | **事务边界小于结算边界。** 效果挂到目标身上在事务外（`ProcessPending`），跑阶段图才开事务（`ActivateEffects`），两者之间可因时间切片跨帧让步。「失败不留部分结果」在单窗口内成立，跨窗口不成立 | `EffectApplicationSystem.UpdateSlice` |
| **B4** | 标签授予绕过事务直写世界，靠外层三个快照手工补偿。同一实体的标签有两套写入与两套回滚机制 | `EffectApplicationSystem` → `EffectTagContributionHelper.PrepareGrantToEntity` |
| **B5** | **延迟触发器队列静默丢弃。** 主缓冲与溢出缓冲皆满时直接 `return`，唯一痕迹是三个 fuse 标志，而这三个标志**全仓无任何读取点**。直接违反「禁止固定容量溢出时静默丢弃」 | `DeferredTriggerQueue.cs` |
| **B6** | **热路径扩容。** 生成实体路径三处 `Reserve(Count + OverflowCount + N)`，`Reserve` 是真扩容（换两条更大数组 + 搬运溢出环，只增不减），把本该抛的 `CapacityExceededError` 变成静默堆增长。相邻分支在服务缺失时是正确抛错的 | `RuntimeEntitySpawnSystem.cs`；`EffectRequestQueue.Reserve` |
| **B7** | **死信号：容量遥测整条断线。** `EffectRequestQueue._dropped` 全文无自增，`DroppedCount` 恒为 0，而测试在断言它等于 0（恒真空断言）；`_budgetFused` 置位后终身为真、无人读、不复位；`BuiltinHandlerExecutionContext.DroppedCount` 的唯一写入者入参从不被赋值 | `EffectRequestQueue.cs`；`TargetResolverFanOutHelper.ValidateAndCollect` |
| **B8** | **图 VM 关系查询算子静默截断。** 出边超 `MaxTargets = 256` 的实体，多出的目标无声消失（`CollectOutgoing` 满即 break、只返回 count 不返回 dropped；`SetCount` 再截一刀）。对照组就在同一文件里：空间查询算子完全合规（默认 RequireComplete、dropped > 0 即抛、AllowTruncated 必须显式授权）。同一份策略只落实了一半 | `GasGraphOpHandlerTable`（关系查询 handler）；`RelationshipRuntime.CollectOutgoing` |
| **B9** | **变更通知建立在 tag component 加删上。** `AttributeAggregateDirty` / `GameplayAttributeChangedBits` 加走直接 `World.Add`、删走 CommandBuffer，每个当帧变化的实体每帧固定两次 archetype 迁移；角色 archetype 约 1.3KB/实体，每次迁移 memcpy 约 1.3KB。与红线「禁止热路径结构变更」正面冲突。`DirtyFlags` 那种常驻位掩码组件已证明替代路径可行 | `AttributeMutationOps.MarkPresentationChanged`、`EffectApplicationSystem`、`EffectProposalProcessingSystem`、`GasGraphRuntimeApi`、`ClearPresentationFlagsSystem` |
| **B10** | **dirty 是双钥匙，只置一把静默失效。** 延迟触发器系统只从队列取实体、不做全表扫。旁路写既不置 mask 也不入队 → 属性变化触发器永不触发；且不刷 `AttributeLastSnapshot`，导致**下一次合法写入报出的 `OldValue` 把旁路那段 delta 一起吞进来**，污染 delta 归因 | `DeferredTriggerCollectionSystem` |
| **B11** | **越界与非法属性 id 静默吞写。** `SetCurrent(-1)` / `GetCurrent(-1)` / `SetCurrent(64)` 全部静默返回不抛；而按名字取 id 失败时返回的正是 `-1`。喂 `-1` 进去的结果是读 0、写被吞、`before == after` 于是连 dirty 都不打，全程零异常零日志。同族的 `GameplayTagContainer.ValidateTagId` 对非法 id 是**抛异常**的 | `AttributeBuffer.cs` 对照 `GameplayTagContainer.cs` |
| **B12** | **两个兄弟注册表的有效性约定相反。** 属性表 `_nextId = 0` / `InvalidId = -1`；标签表 `_nextId = 1` / `InvalidId = 0`。6+ 处代码套用标签表约定去判属性（`if (attributeId > 0)`），于是**第一个注册的属性**在这些路径上静默不可用，而「第一个」是谁取决于 mod 加载顺序 | `AttributeRegistry.cs` vs `TagRegistry.cs`；`BuiltinHandlers.HandleApplyForce` 等 |
| **B13** | **属性与 sink 注册表生产从不 Freeze。** id 分配依赖 mod 加载顺序 → 换一组 mod 或换加载序，同一个属性名拿到不同 id，威胁存档/回放确定性。而 `ProgressionIdRegistry` / `ContextGroupIdRegistry` / `GraphIdRegistry` 都规规矩矩 Freeze 了——属性与 sink 是唯二例外，且违反 `gas-layered-architecture.md` 明文要求 | 无生产调用点 |
| **B14** | **寄存器索引有三套互不相认的占位机制。** ① bump 分配器（无 liveness 复用）；② 作者 `PinRegister`（pin 小于当前游标时**无诊断**，静默与已分配寄存器别名）；③ 硬编码常量 scratch（`TargetListGet` 与 `validOutput` 都写死 B[31]）。而唯一做对的 `AllocateSugarScratches` 按 used-set 找空位，但它的 used-set **不含 B[31]**。再叠上宿主侧 ABI 槽（B[0] Validation 判定、F[0] Score 分数、I[0] BT sensor），这四个槽的 SSOT 不在代码里任何一处 | `GraphControlFlowCompiler.cs`、`.Linear.cs`；`GraphExecutor.cs`；`BehaviorTreeOps.cs` |
| **B15** | **`Programs` 字段除测试专用入口与嵌套子帧外没有任何宿主填写**，而 `HandleInvokeScript` 首行 `if (s.Programs == null) throw`。即：所有生产执行路径都**写得出** `InvokeScript`，一个都**执行不了**。范围包含 Effect 阶段、Query 输出、Performer 条件、Aim 预览，不止 L2 三个宿主 | `GraphExecutionState`（15 字段 ref struct、无构造不变式、11 个手工构造点）；`GasGraphOpHandlerTable.HandleInvokeScript` |
| **B16** | **掉出程序尾部被当成「成功停机」。** `(uint)pc >= (uint)program.Length` → `Halted` + 返回成功，负 pc 也走这条。任何坏跳转都被报成正常完成。注释把它写明为「既有 GAS 图掉出尾部算成功完成」——一条兼容性 fallback 焊进了 VM 内核。且跳转目标从不在装载期校验 | `GasGraphOpHandlerTable.ExecuteSliceCore` |
| **B17** | **程序校验发生在每次执行而非登记时。** `RequireAllowed` 是 O(程序长度) 全扫、每次执行都跑（登记时已跑过一次）；`AnalyzeScratchUsage` 是第二遍 O(n) 全扫，且它是**全系统唯一的寄存器越界校验**——BT / HFSM / Level / Performer / Aim / ReturnWriter 一概没有，越界只表现为 span 的 `IndexOutOfRangeException` 而非失败关闭诊断 | `GraphKindOperationPolicy`；`EffectPhaseExecutor.AnalyzeScratchUsage` |
| **B18** | **能力矩阵与前门是两份手工数据。** 前门是三张手写 op 列表（线性 86 / 查询 41 / Script 11），一张线性表被 4 个 kind 共用而这 4 个 kind 有 3 套策略。缝隙可量化：Score/Validation/Derived 有 19–20 个「前门允许但策略拒」（策略在装载期赢，所以是失败关闭，但作者拿到的诊断是「操作不被 kind 允许」而非「这个节点在这个方言里不存在」）；各 kind 另有 33–89 个「策略允许但前门写不出」。**没有任何测试断言「前门 ⊆ 策略」** | `GraphKindOperationPolicy`（含 3 处硬编码例外）；`GraphControlFlowCompiler.Linear.cs` / `.Query.cs` |
| **B19** | **Script 方言窄到「感知」必须写在 C# 里。** 只有 11 个可写 op（int 数学 + 控制流），读不到属性、读不到黑板、做不了 float 运算、发不了查询，于是行为树必须用 C# sensor feed 把结果塞进 I[0]。而三个 L2 宿主之所以能只填 `I/B/CallStack` 就跑（B15），**正是因为 Script 这么窄**——往 Script 矩阵加任何一个读属性/黑板/查询的 op，三个宿主立刻崩（`F`/`E`/`Targets` 是零长 span，`Api` 与 `Programs` 是 null）。**这两件事必须同时改** | `GraphControlFlowCompiler.cs`（Script 内联列表）；`BehaviorTreeOps.cs`；`AiConfigModels.cs`（无 BT/HFSM 条目） |
| **B20** | **L2 没有作者面。** AI 配置覆盖 Utility / GOAP / HTN / atoms / projections / bindings，**没有行为树、没有状态机**。拓扑只能在 C# 里用工厂造，而这些工厂连同玩法语义（哨兵状态机、巡逻-追击-攻击树、11 个 `bt.*`/`hfsm.*`/`level.*` 名字）都住在 Core。「一切皆 Mod」在 L2 上不成立。这 11 个名字同时又是 `action_lib.json` 的全部条目名——同一份内容两个 SSOT | `HfsmDefinition.cs`、`BehaviorTreeOps.cs`、`BehaviorTreeFactory.cs`、`LevelScriptPrograms.cs` |
| **B21** | **一个程序集装下全部层。** `Ludots.Core.csproj` 同时是 L0 VM、L1 编译器、L2 调度器、Presentation、Input、Spatial、Navigation（只有 Physics2D 三个项目被拆出去）。所有「墙」在编译期都不存在，只能靠运行期检查与文本扫描事后追认。**这是其他所有结构性边界问题的根因** | `src/Core/Ludots.Core.csproj` |
| **B22** | **Core/Mod 端口被一跳绕开。** `IModContext` 本身设计干净（VFS / FunctionRegistry / SystemFactoryRegistry / TriggerDecorators / OnEvent / Log），但 `ScriptContext.GetEngine()` 交出具体 `GameEngine`，mods 里 205 处在用；另有 100 处直接 `RegisterSystem` 进 6 个组。Mod 还直写 Core 进程级静态：`TeamManager`(7 mod)、`TagRegistry`/`AttributeRegistry`(4)、`GraphIdRegistry.Clear`(5)、`SetCoordinateConverter`(3) | `ScriptContextExtensions.cs`；`mod-architecture.md` |
| **B23** | **`SystemGroup` 顺序有两份列表、零顺序守卫、漏组静默失效。** 真正决定执行顺序的是 `PhaseOrderedCooperativeSimulation.PhaseOrder` 数组，enum 声明顺序对执行无影响；两份今天一致但无交叉校验。守卫用 `Is.EquivalentTo`（集合相等，**不比较顺序**），把 `Cleanup` 挪到最前面它照样绿。某个组在 enum 里有、在 `PhaseOrder` 里漏了，注册进去的系统每帧都不跑且无诊断 | `PhaseOrderedCooperativeSimulation.cs`；`ArchitectureGuardTests.SystemGroup_MustMatchDesignDocument` |
| **B24** | **守卫层自身违反它守的合同**，且违反 DRY：两个 `ArchitectureGuardTests` 同名同 namespace、37 个方法逐字节相同的复制，合计 144 次 `ReadAllText` + 6 次反射；而 `gas-order-input-runtime-contract.md` 白纸黑字写着「不得用 `ReadAllText + Contains` 复述源码实现；需要静态门禁时使用编译后的 API、Roslyn/analyzer」 | `src/Tests/ArchitectureTests/Governance/ArchitectureGuardTests.cs` vs `src/Tests/GasTests/Integration/ArchitectureGuardTests.cs` |
| **B25** | **静态注册表的 freeze 合同不一致。** 属性表 `Clear()` 在冻结时抛；标签表 / 效果模板 id 表 / 技能 id 表 / 图 id 表全部**静默解冻**。`GraphIdRegistry.Clear()` 连 `_frozen` 一起复位，且是 `public static`、mod 可调。实例级 `GraphProgramRegistry` 与静态 id 表并存 → 同进程二次装载时 id 错位是**必然**而非偶发 | `GraphIdRegistry.cs`、`TagRegistry.cs`、`EffectTemplateIdRegistry.cs`、`AbilityIdRegistry.cs` 对照 `AttributeRegistry.cs` |
| **B26** | **「测了不断言」的假防线成片存在。** `GraphPerfTests.cs` 整个文件零断言（跑 100 万次图 VM、算出分配字节数、`Console.WriteLine`、结束）——这是图 VM 唯一的零分配基准；`GasBenchmarkTests.cs` 8 个 benchmark、5 个测分配，全文件只有 2 句关于触发器计数的断言；`PhaseExecutor_HighVolume_ReportsThroughputAndGc` 注释写「<10μs」断言写 `< 50.0`；`BlackboardOps_Stress_MassWriteRead_ZeroGc` 名字带 ZeroGc 但唯一性能断言是 `TotalSeconds < 30.0`，且**测量区间内每次迭代 `new int[]` 自己污染自己**；`GraphBehaviorPressureMatrixTests` 全文件 3 句断言全是 `File.Exists`（只保证 CSV 被写出，不保证数字在任何范围内）。真有牙齿的零分配断言只有 6 条 | 上述文件 |
| **B27** | 性能门槛命名与断言不符：行为树那条名叫「五毫秒」实际断言 `< 15.0` 且裸断言无消息；脚本版状态机同样名说 5 实测 15（消息如实写了 15）。另两条名实相符。#914 声称还原的思考波闸门（`over5ms == 0` + avg/p95 < 15）**确认是真还原** | `BehaviorTreeRuntimeTests.cs`、`FsmRuntimeTests.cs`、`GraphBehaviorArenaAcceptanceTests.cs` |
| **B28** | **零分配覆盖有系统性缺口**：4 条真断言覆盖 proposal / 瞬时效果 / 关系事务 / listener 派发，**完全没覆盖持续效果的 tick 循环**——`EffectApplicationSystem`、`AttributeAggregatorSystem`、`EffectLifetimeSystem`、`TimedTagExpirationSystem`、`DeferredTriggerCollectionSystem` 一个都没进零分配测试。而 B9 那个「每帧两次迁移」恰好就发生在这些没被测的系统里 | `AllocationTests.cs` |

### 3.4 Minor 与债务（择要）

| # | 项 |
|---|---|
| C1 | `BuiltinHandlers` 的位移替换是全 GAS 唯一 lambda 查询，捕获 4 个可变局部 → 每次调用一个 display class + 一个委托；同函数还有直接同步 `world.Remove`，而同组件在另一系统走的是 CommandBuffer |
| C2 | `MaterializeEffect` 建实体后最多连做 8 次 `World.Add`（8 次连续 archetype 迁移），而 factory 里已有预建原型 + CommandBuffer 的合规重载没被用 |
| C3 | `EffectPhaseSideEffectTransaction.Commit` 是 `AttributeMutationOps` 的平行重实现（整 struct 赋值 + 手写 dirty 循环），两套语义需人工同步 |
| C4 | `StagePresentationEvent` 缺服务时静默 no-op，而同类 `StageEffectRequest`/`StageSpawnRequest` 抛异常；三个 fan-out builtin 缺服务时静默返回零结果 |
| C5 | `PrepareCommitState()` 在 `try` 之外调用，`Commit` 非自洽，全靠三个调用方各自 catch 兜底 |
| C6 | `EffectRequestQueue.Clear()` 静默丢弃未消费项 + 回灌溢出环；Core 生产系统不调它，但 showcase mod 有 4 处在调（换展厅 1 处 + 3 个 driver 每拍） |
| C7 | `AbilityExecInstance.SetItem` 定长溢出静默 `return`，丢弃时间线条目 |
| C8 | `SystemFactoryRegistry` 重名后写覆盖只 Warn（与「禁止 registry 重复注册后采用最后一次写入」冲突）；找不到 factory 只 Warn |
| C9 | `GraphControlFlowCompiler` 是 `public static partial class`，`Compile` 公开可绕前门直连（今天只有测试这么干，无 analyzer 阻止生产代码这么干）——「前门」是命名约定不是可见性约束 |
| C10 | 图程序有四个来源：配置管线、mod 自扫资产、**C# 字符串常量内嵌**（Float mod 还用 `.Replace` 做 C# 侧文本模板，与「不支持编译期文本宏」冲突）、LSW 热改补丁 |
| C11 | `GraphProgramSymbolPatcher.Patch` **不幂等**：把符号索引原地覆写成解析后 id，第二次跑会把 id 当符号索引再解析——热改路径值得单独查 |
| C12 | 指令编码无 operand schema：`Flags` 承担至少 7 种含义，`Dst` 承担 4 种；语义必须在编译器 emit / 符号 patch / handler 三处手工对齐，这是 B14 那类 bug 的温床。relationship typeId 被 byte + 哨兵封在 254 以内 |
| C13 | `GraphExecutionStatus` 把「未开始」与「预算耗尽挂起」压在同一个 `Running` 上，而行为树续跑判断只认 `Yielded` → action 叶子因预算耗尽返回 Running 时，下一拍寄存器清空、cursor 归零、**脚本从 pc=0 静默重跑，已产生的副作用重放** |
| C14 | `LoadConfigInt` 与 `LoadConfigEffectId` 是同一个函数体，零语义差异；120 这个数字被这类别名和「静态/动态 × 单体/扇出」组合撑大，真实 kernel 家族约 30–40 |
| C15 | `RequireNoYield` 每次 `InvokeScript` 运行时对子程序做 O(n) 线性扫描（本该在装载期）；同处 `RequireKind` + `TryGetProgram` 两次字典查找 |
| C16 | 聚合循环不用 `DefinedMask` 剪枝，每脏实体每 pass 约 320 次定长迭代，无论它定义了 2 个还是 60 个属性 |
| C17 | `GameplayAttributeChangedBits` 用 64 字节表达 64 bit（`Clear`/`IsAnyBitSet` 都是 O(64) 循环），与 `DirtyFlags.AttributeDirtyMask`（一个 `ulong`）语义重复且占 8 倍空间 |
| C18 | `MAX_ATTRS = 64` 在三处独立定义互不引用；`"Health"`/`"MoveSpeed"` 等字面量 20+ 处（含 Core）无集中常量表；评估/投影层每次调用按字符串取 id |
| C19 | `ExtensionAttributeRegistry` 是死代码：发 10001+ 的 id，而 `AttributeBuffer` 对 ≥64 的 id 一律静默 return，全仓无消费者。一整套挂在 Phase 0 上、每帧遍历空队列的平行 id 空间 |
| C20 | `EntityLifecycleAtomicOps.CopyAttributeSlice` 在循环外取 `ref AttributeBuffer`，循环内首次写入触发 archetype 迁移 → 第 2 次及以后迭代读的是旧 chunk 失效内存（`AttributeSliceCount >= 2` 且目标尚无 `GameplayAttributeChangedBits` 时命中） |
| C21 | `GraphRuntime` 不许引用 GAS 的唯一分层守卫已被 `GraphControlFlowDocument.cs` 的传递依赖穿透；L1 编译器还依赖 `Presentation.TagDisplay` |
| C22 | 跨层组件所有权无机制表达：`PresentationStableId` / `PresentationDestroyPending` / `CullState` 三方读写、无声明 owner（`CullState` 由模拟创建、表现写、输入读）；`entity-simulation-layering.md` 的「单写真相规则」在代码里没有任何执行体 |
| C23 | 测试树里有第二执行器（Roslyn 生成 C# → collectible ALC），带对拍测试、属受控 spike，但「禁止第二执行器」的合同没为它写豁免 |
| C24 | `SystemGroup` 正式顺序表（文档与任务书）缺 `RuntimeEntityBinding`，而它有 9 处生产用法 |
| C25 | `OwnershipResolver` 在热路径 `Array.Resize` 扩容重试（摊销、且恰好补上了 B8 缺失的 dropped 语义） |
| C26 | `GasClockSystem.FireTurnAdvanced` 每次 `new ScriptContext()` 且装箱（只在手动/回合步进，不在固定 tick） |
| C27 | `EffectTemplateRegistry.TryReplaceHotNumericField` 在冻结后改写模板数组（仅 duration/period，仅 LSW 调用） |
| C28 | `ResetAbortedStructuralCommands` 在回滚路径 `new CommandBuffer`（错误路径分配） |

---

## 4. 场景（这些问题玩家/作者会怎么撞上）

1. **技能作者写了一张会自己调自己的图**（或 A→B→A 的环）。加载全绿，一进战斗游戏直接消失——不是报错弹窗，是进程没了。日志里什么都没有，因为栈溢出不给你写日志的机会。

2. **策划配了一个不夹上限的属性**（比如移速），给它挂一个永久 +18 的 buff，然后在某处直接写了一个移速值。当场看是对的，过了若干秒、玩家做了一件毫不相干的事之后，那个值自己变回去了。查不出来，因为写入的地方和变回去的时刻没有任何因果可见性。

3. **策划把某个属性配成 mod 里第一个注册的**。于是几条内置路径（比如施加冲力）对这个属性静默失效——不报错，就是没效果。换个 mod 加载顺序，症状转移到另一个属性上。

4. **作者在查询图里写了一条调用，指向一个巡逻动作**。查询本该是纯的，但这条能编译、能加载、能执行。技能结算里混进了一个会等一拍的动作。

5. **高压帧**（大量属性变化）：延迟触发器队列满了，"血量低于 30% 触发被动"这类联动**静默不响**。面板上的数字是对的，所以第一反应会去查被动逻辑，而真正的原因是触发器被丢了。

6. **一次命中打了很多目标，中途某个目标被别的效果销毁**。事务回滚在中途抛出，留下一半回滚的世界——属性回滚了、关系没回滚。

7. **当时：** 玩家在启动器列表里看到八张打了"已退役"灰标的旧展厅卡，卡底还是有可复制的启动命令；其中五间一进门就把引擎的图编号表清空。**现在：** 这些图能力家族房间已删除，启动器里没有它们的退役卡片。

8. **玩家点进"两段伤害叠在一起"，字幕说血条按总和往下掉**。数字是真图算的，但把它落到世界上那一步是脚本直接改的；而且如果这一刀本该把血打到 0，代码会静默把它回卷成满血。

---

## 5. 边界

**做了**

- 以正式架构文档为判据，逐条证伪 GAS 与图 VM 的分层、事务性、属性链、能力模型、ECS 纪律
- 把"结构性问题"与"实现瑕疵"分开定性（后者不该按前者的成本去修）
- 对六条推导性断言做实测证实（§6），不以静态阅读替代运行证据

**没做**

- 不改任何生产代码（本轮零生产改动）
- 不审 UI 面板债（#886 / #893）、表现层改名、#723 评分预算
- 不重开已裁决的产品争论（Duration/Period 在效果壳上；FuncLib 纯 / ActionLib 可挂起；单节点展厅是玩家门）
- 不对"拆程序集"给出具体切分方案——那需要单独设计，见修复计划 S12

**刻意保留的判断**

- 图 VM 执行核心、编译器前端、组件 SoA 布局、`AddFixed` 容量模式、派生属性围栏、`DirtyFlags` 失败关闭设计，**都是对的，修复时不要顺手改**
- 120 个 opcode 这个规模**不是**问题；分派表撑得住再加 100 个。真正到临界点的是寄存器索引归属、装载期校验器缺位、以及调用的能力传播

---

## 6. UAT

以下六条是本轮**实测**（非静态推导）的验收记录。探针源码见 PR 附件 `attribute_probe_Program.cs` 与 `recursion_probe.cs`，均为 `/tmp` 下独立项目直连 `src/Core/Ludots.Core.csproj`，未修改工作区任何源码。

```gherkin
Feature: 一张图不能把游戏弄没了
  作为技能作者
  我希望我写错的图被当场拒绝
  以便我不会因为一个笔误让所有人的游戏直接退出

  Scenario: 自己调自己的图必须被拒绝            # 未过（实测进程被杀）
    Given 我写了一张脚本图，它唯一做的事就是调用自己
    When 这张图被登记进图注册表
    Then 登记必须失败并告诉我这里有一个调用环
    And 游戏不应该能带着这张图启动

    实测：注册表接受了它（TryGetProgram 返回 True，无任何装载期环检测）；
          执行时递归 1495 层后栈溢出，进程退出码 134，try/catch 无法进入。

Feature: 我写进属性的数字不会自己变回去
  作为策划
  我希望我通过正式接口写进去的数值就是最终生效的数值
  以便我不用怀疑是不是引擎在背后改我的数

  Scenario Outline: 直接写入的当前值应当保持   # 部分过（取决于属性配了什么约束）
    Given 一个基础值 100 的属性，挂着一个永久 +18 的修正
    And 我通过正式接口把当前值写成 50
    When 属性重算发生
    Then 当前值应当仍然是 50

    实测：
      | 属性是否夹上限 | 是否重新标脏 | 重算后 | 结果 |
      | 是（血量这类） | 是           | 50     | 过   |
      | 否（移速这类） | 是           | 118    | 未过 —— 写进去的 50 被静默丢弃 |
      | 否             | 否           | 50     | 过，但只是暂时的：覆盖被推迟到别的事件把该实体标脏时才发生 |

Feature: 写错属性名不应该悄无声息
  作为策划
  我希望我把属性名拼错时能立刻知道
  以便我不用靠"为什么这个效果没反应"去反推

  Scenario: 非法属性编号必须失败关闭          # 未过
    Given 我引用了一个没有注册的属性名
    When 系统按这个名字取编号，然后拿这个编号去写值
    Then 写入必须失败并点名这个属性名

    实测：取编号返回 -1；SetCurrent(-1) 静默返回、GetCurrent(-1) 返回 0、
          SetCurrent(64) 静默返回（上限 64）。三者均不抛，且因为 before == after，
          连脏标记都不打——全程零异常零日志。
          对照：同族的标签容器对非法编号是抛异常的。

Feature: 属性的编号不该看 mod 装载顺序
  作为存档与回放的使用者
  我希望同一个属性名在任何一次运行里拿到同一个编号
  以便旧存档在新版本里读出来还是同一个意思

  Scenario: 第一个注册的属性也必须可用      # 未过
    Given 某个属性恰好是本次运行中第一个被注册的
    When 内置的施加冲力之类的路径去读它
    Then 它应当和其他属性一样正常工作

    实测：清空注册表后第一次注册拿到编号 0，而"无效"标记是 -1；
          若干路径用「编号大于 0」判有效，因此对编号 0 的属性静默跳过。
          换一组 mod 或换加载顺序，受害者就换一个。

Feature: 容量到顶时要说话
  作为运维与开发
  我希望系统在压力到顶时明确报错
  以便我知道要调容量，而不是以为功能坏了

  Scenario: 延迟触发器队列满时必须失败关闭    # 未过
    Given 一帧内产生的属性变化触发器超过了队列容量
    When 队列的主缓冲与溢出缓冲都已满
    Then 系统必须抛出并点名容量与来源

    实测：直接 return 丢弃，只置一个 fuse 标志；
          该标志有 public getter 但全仓无任何读取点。
          同型死信号还有效果请求队列的丢弃计数器（永不自增，而测试在断言它等于 0）。

Feature: 零分配的承诺要有人守着
  作为性能负责人
  我希望名字里写着零分配的测试真的在断言零分配
  以便防线退化时 CI 会红

  Scenario: 零分配断言必须真实存在            # 部分过
    Given 一批名字包含 ZeroAlloc / Benchmark / ZeroGc 的测试
    When 我检查它们的断言
    Then 每一条都应当对分配量下断言

    实测：真有牙齿的零分配断言 6 条（AllocationTests 4 条 + MathOpsChain 1 条
          + EntityCollection 1 条），全部通过。
          而图 VM 唯一的零分配基准整个文件零断言（只 Console.WriteLine）；
          另一批 benchmark 测了分配但全文件只有 2 句与分配无关的断言。
          持续效果的 tick 循环（效果应用、属性聚合、生命周期、标签过期、
          触发器收集）完全没有零分配覆盖。
```

**另有五路静态审查的完整逐条符合性判定**，因篇幅未全部展开于 UAT，见 §3 各表的证据列。

---

## 7. 相关文档

- 修复任务分配：[GAS + Graph VM 架构修复计划](gas_graph_architecture_fix_plan.md)
- 前序：[PR932 main 图能力收口架构审计](pr932_graph_landed_architecture_audit.md)
- 前序：[PR911 FuncLib/ActionLib 架构审计](pr911_funclib_actionlib_architecture_audit.md)
- 判据：[GAS 分层架构](../../gitbook/architecture/gas-layered-architecture.md)
- 判据：[图分层、流程与行为](../../gitbook/architecture/graph-layering-flow-and-behavior.md)
- 判据：[图复用库合同](../../gitbook/architecture/graph-funclib-actionlib-contract.md)
