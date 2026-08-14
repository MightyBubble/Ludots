# GAS Graph 能力全景与 Showcase 设计

本篇讲清楚两件事：

- ludots 的 GAS Graph（Graph VM）到底能做什么，边界在哪。
- 如何设计一套 showcase，把 graph 的全部能力域覆盖住，并且每条能力都可被验收。

阅读顺序建议：第 1-3 章理解能力模型，第 4 章看能力清单，第 5 章之后是 showcase 设计。

参考实现：

- 指令集枚举：`src/Core/NodeLibraries/GASGraph/GraphOps.cs`
- Handler 分发表：`src/Core/NodeLibraries/GASGraph/GasGraphOpHandlerTable.cs`
- 编译器 / 校验器：`src/Core/NodeLibraries/GASGraph/GraphCompiler.cs`、`GraphValidator.cs`
- 执行器与状态：`src/Core/NodeLibraries/GASGraph/GraphExecutor.cs`、`GraphExecutionState.cs`
- VM 限额：`src/Core/NodeLibraries/GASGraph/GraphVmLimits.cs`
- 运行时 API 边界：`src/Core/NodeLibraries/GASGraph/IGraphRuntimeApi.cs`、`Host/GasGraphRuntimeApi.cs`
- Op 速查表：`docs/architecture/interaction/features/_common/graph_ops_reference.md`

---

## 1 Graph VM 是什么

GAS Graph 是一台**寄存器式的确定性小虚拟机**，跑在 Effect Phase 内部。它的定位不是通用脚本，而是「把战斗、AI、关卡逻辑从 C# 硬编码里搬到数据里」的执行层。

一句话概括职责边界：

> Graph 负责**读状态、算数值、选目标、下决策、派发效果**；不负责**结构变更**（创建/销毁实体、挂载组件）。

结构变更必须交给 `RuntimeEntitySpawnQueue` 或 `CommandBuffer`，在 Phase 之外落地。这条边界是 graph 能保持确定性和零分配的前提。

### 1.1 寄存器模型

Graph 程序操作四组类型化寄存器，指令里的 `dst` / `a` / `b` 都是寄存器下标，不是变量名：

| 寄存器组 | 类型 | 典型用途 |
|---|---|---|
| `B[]` | bool | 比较结果、分支条件、有效性标志 |
| `I[]` | int | 计数、templateId、任务 id、枚举态 |
| `F[]` | float | 属性值、伤害、距离、系数 |
| `E[]` | Entity | 施法者、目标、上下文实体、查询结果单体 |

外加一条隐式的 `TargetList`：空间查询和关系查询的结果集容器，由 filter / sort / limit / agg 系列指令链式加工。

固定寄存器约定（EffectContext 映射）：

```
E[0] = EffectContext.Source        施法者 / 事件来源
E[1] = EffectContext.Target        主目标
E[2] = EffectContext.TargetContext 附加上下文（AoE 中心、链式原始目标、Viewer）
```

### 1.2 执行模型

- **指令式，非图形化连线**：程序是一条线性指令数组，控制流靠 `Jump` / `JumpIfFalse` 的相对偏移实现。这让编译产物是紧凑的数组，遍历无指针跳转。
- **单帧内跑完**：一次 `Execute` 走完整个程序，不跨帧挂起。长时行为（倒计时、任务持续）用 Blackboard 存进度，靠 Phase / tick 重入实现。
- **有限额**：`GraphVmLimits` 约束指令步数、寄存器数、TargetList 容量。超限即中断并报诊断，不会失控。
- **零分配热路径**：执行期不产生 GC 分配，这是 50k 规模 benchmark 的硬指标。

### 1.3 编译与符号解析

作者写的是 JSON（op 名 + 寄存器 + 立即数 + 符号名），运行时拿到的是数值化程序：

```
JSON 程序  →  GraphCompiler  →  GraphValidator  →  符号 patch  →  可执行 program
                                                    ↑
                              GasGraphSymbolResolver / GraphIdRegistry
                              （属性名、tag 名、effect 模板名、BB key → 数值 id）
```

符号解析（`Host/GasGraphSymbolResolver.cs`、`Host/GraphProgramSymbolPatcher.cs`）意味着 mod 作者写 `"Health"` 而不是 `42`，同时运行时仍然按整型 id 走。校验器在加载期就拦下未知符号、越界寄存器、坏跳转，不留到运行时崩。

---

## 2 Graph 挂在哪里跑

同一台 VM 被多个宿主复用，这是理解「graph 能做什么」的关键：能力域取决于挂载点。

| 挂载点 | 触发时机 | 典型职责 | 参考 |
|---|---|---|---|
| Effect Phase Handler | Effect 生命周期某个 Phase 的 Pre/Main/Post | 伤害计算、减伤、护盾吸收、效果派发 | `EffectPhaseGraphBindings.cs` |
| Phase Listener | 监听特定 Tag/TemplateId 的 Phase | 吸血、反甲、触发式被动 | `EffectPhaseListenerBuffer.cs` |
| 派生属性图 | 属性聚合后重算 | 攻击力 = f(力量, 装备, buff) | `AttributeDerivedGraphBinding.cs` |
| AI Brain | 每个 AI tick | FSM 状态切换、BT 任务选择 | `mods/showcases/graph_*` |
| 关卡蓝图 | 触发区 / 事件 | 门、机关、目标推进 | `graph_level_blueprint` showcase |
| 放置校验 | 建造 / 下令时 | 位置合法性、吸附、射程钳制 | Op 402-408 |
| UI / 事件求值 | 事件评估、可见性判定 | viewer 相关的可控性、知识投影 | Op 410-422 |

Effect Phase 顺序（graph 可挂载的时间轴）：

```
OnPropose → OnCalculate → OnResolve → OnHit → OnApply → OnPeriod → OnExpire → OnRemove
每个 Phase 内：Pre → Main → Post → Listeners（priority 升序）
```

---

## 3 能力域总览

把指令集按「能干什么」重新归类，得到 12 个能力域。这也是后面 showcase 覆盖矩阵的行。

| # | 能力域 | 能做什么 | Op 段 |
|---|---|---|---|
| D1 | 常量与控制流 | 分支、循环、早退 | 1-3, 6-7 |
| D2 | 实体加载 | 拿到施法者、目标、上下文、viewer | 4-5, 320-322, 410 |
| D3 | 数值运算 | 浮点/整型算术、比较、钳制、随机 | 20-35 |
| D4 | 属性读写 | 读属性、走 Modifier 改属性、派生属性直写 | 10, 210, 330-331 |
| D5 | Tag 判定 | 持有判定、状态门控 | 33 |
| D6 | Blackboard | Phase 间传值、跨 tick 存进度 | 300-305 |
| D7 | 配置参数 | 从 EffectTemplate 读系数，一图多用 | 310-312 |
| D8 | 空间查询 | 圆/锥/矩形/线/Hex 选目标 + 过滤排序截断聚合 | 100-132, 380-393 |
| D9 | 关系图查询 | 实体间连边的建立、度量、遍历、聚合 | 360-376, 394-397 |
| D10 | 效果与事件派发 | 单体/扇出施加效果、移除效果、发事件 | 200-206, 220 |
| D11 | 生命周期组合 | 事务化生成/消耗、调用 builtin | 400-401, 408 |
| D12 | 放置与拓扑 | 位置校验、吸附、控制域、知识投影 | 402-407, 420-422 |

---

## 4 能力域详解

### D1 常量与控制流

`ConstBool/ConstInt/ConstFloat` 写立即数进寄存器；`Jump` 无条件跳，`JumpIfFalse` 在 `B[a]==0` 时跳。偏移是相对的，所以程序可整体平移拼接。

循环用「比较 + 条件跳回」手写。配合 `TargetListGet` + `AggCount` 就是标准的遍历目标列表模式：

```
I[0] = 0                     ; index
I[1] = AggCount              ; count
loop:
  B[0] = CompareLtInt(I[0], I[1])
  JumpIfFalse B[0] → end
  E[3] = TargetListGet(I[0])
  ... 处理 E[3] ...
  I[0] = AddInt(I[0], 1)
  Jump → loop
end:
```

### D2 实体加载

`LoadCaster` / `LoadExplicitTarget` 面向能力执行；`LoadContextSource/Target/TargetContext` 面向 Effect 上下文；`LoadViewer` 面向「站在谁的视角求值」的场景（可见性、UI 判定）。`SelectEntity` 从 TargetList 里取单体。

### D3 数值运算

完整的浮点四则 + `Min/Max/Clamp/Abs/Neg`，除零安全（→0）。整型有 `AddInt` 和 `CompareLtInt/CompareEqInt`。`CompareEqEntity` 做实体同一性判定。`RandomFloat01` 提供受控随机——注意它会破坏严格重放确定性，需要时用固定种子或避开。

`ClampFloat` 三寄存器形式：`F[dst] = clamp(F[a], F[b], F[c])`。

### D4 属性读写

三条路径语义不同，别混：

- `LoadAttribute(E, key)`：读任意实体的属性当前值。
- `ModifyAttributeAdd`：走标准 Modifier 聚合管线，可被其他 buff 叠加影响，是**战斗改值的正路**。
- `LoadSelfAttribute` / `WriteSelfAttribute`：不需要 EffectContext，直接读写 Caster；`Write` 是 `SetCurrent` 直写，**绕过 Modifier 聚合**，专供派生属性图使用。用在战斗伤害上会破坏聚合语义。

### D5 Tag 判定

`HasTag(E, imm)` 是 graph 里的状态门控原语。CC、免疫、阶段标记全靠它分支。Tag 容器是 256-bit 定容位集，判定是常数时间。

### D6 Blackboard

per-entity 的 float/int/Entity 键值存储。两个用途：

1. **Phase 间传值**：OnCalculate 算出 `DamageAmount`，OnApply 的减伤 Listener 读它、写回 `FinalDamage`，Main 再读 `FinalDamage` 扣血。
2. **跨 tick 存状态**：AI 的当前状态、任务 id、剩余倒计时都存 BB，下个 tick 重入时读回来。这是单帧 VM 能表达持续行为的机制。

伤害管线标准 key：`DamageAmount`、`DamageType`、`IsTrueDamage`、`FinalDamage`、`MitigatedAmount`。

### D7 配置参数

`LoadConfigFloat/Int/EffectId` 从 EffectTemplate 的 `ConfigParams` 读值。意义是**一份 graph 服务 N 个效果模板**：同一套「按系数造伤害」的逻辑，火球读 `ratio=1.2`，冰锥读 `ratio=0.8`，不需要复制程序。`LoadConfigEffectId` 让「派发哪个子效果」也变成配置项。

### D8 空间查询

查询是一条**流式管线**，不是单指令：

```
来源           → 过滤                        → 排序              → 截断      → 消费
QueryRadius      QueryFilterRelationship       QuerySortStable     QueryLimit   FanOutApplyEffect
QueryCone        QueryFilterTagAny/None        QuerySortByAttribute             AggCount
QueryRectangle   QueryFilterTeam                                                AggMinByDistance
QueryLine        QueryFilterTemplate                                            AggSum/Avg/Max/MinAttribute
QueryHexRange    QueryFilterAttributeRange                                      AggMax/MinEntityByAttribute
QueryHexRing     QueryFilterLayer                                               TargetListGet
QueryHexNeighbors QueryFilterNotEntity
QueryAllMapEntities
QueryFromCollection
```

值得单独点出的：

- `QueryFromCollection`：接实体集合查询基建，把预建集合当查询源，避免每次全图扫。
- `QueryAllMapEntities`：全图源，配合过滤器做全局筛选（如「场上所有残血敌人」）。
- `AggMaxEntityByAttribute` / `AggMinEntityByAttribute`：直接拿「属性最高/最低的那个实体」，不用手写遍历。
- Hex 三兄弟：`QueryHexRange`（范围）、`QueryHexRing`（环，排除内圈）、`QueryHexNeighbors`（六邻）——Hex 地图的一等公民支持。

半径类参数单位是 cm。

### D9 关系图查询

这是 graph 里被低估的一块。它把「实体之间的边」变成可编程数据：

- **写边**：`RelationshipEnsureLink` / `RelationshipRemoveLink`
- **边上度量**：`RelationshipSetMetric` / `AddMetric` / `GetMetric`
- **边上标志**：`RelationshipSetFlag` / `HasFlag`
- **遍历**：`QueryOutgoing` / `QueryIncoming` / `QueryMutual` / `QueryBetweenPair`
- **过滤排序**：`FilterMetricRange` / `FilterFlag` / `SortByMetric`
- **聚合**：`AggSumMetric` / `AggMaxMetric` / `AggMinMetric` / `AggAverageMetric` / `AggMax(Min)EntityByMetric`
- **存在性**：`RelationshipHasLink`

能表达的东西：仇恨表、外交关系、补给链、师徒/雇佣关系、威胁度排序、社交网络传播。遍历结果同样落进 TargetList，可以直接接 D10 的扇出派发——「对所有仇恨值 > 50 的敌人施加标记」是一条直线。

### D10 效果与事件派发

| 形态 | 目标 | templateId 来源 |
|---|---|---|
| `ApplyEffectTemplate` | 单体固定 | 立即数 |
| `ApplyEffectDynamic` | 单体 `E[A]` | 寄存器 `I[B]` |
| `FanOutApplyEffect` | 整个 TargetList | 立即数 |
| `FanOutApplyEffectDynamic` | 整个 TargetList | 寄存器 `I[A]` |
| `FanOutDispatchEffect` | TargetList，source/target/context 按 payload preset 映射 | 立即数 |
| `FanOutDispatchEffectDynamic` | 同上 | 寄存器 |
| `RemoveEffectTemplate` | 移除 `E[A]` 上匹配 templateId 的活跃效果 | 立即数 |
| `SendEvent` | 发事件进 TriggerManager | 立即数 |

`Dispatch` 系列比 `Apply` 系列多的是**上下文映射控制**：链式闪电要求「每一跳的 source 是上一跳的目标」，这靠 payload preset 表达，不用为每种拓扑写新 op。

`SendEvent` 是 graph 与 trigger 系统的接缝，让 graph 能反向驱动关卡逻辑。

### D11 生命周期组合

`BeginLifecycleTransaction` 开启事务化的生命周期操作，`BeginLifecycleConsumeSource` 表达「消耗来源实体」（如建筑升级消耗工程车），`InvokeBuiltin` 调用内建 handler（spawn、投射物、位移等）。

注意这些指令**不在 Phase 内直接改结构**，而是把请求排进队列，由 `RuntimeEntitySpawnSystem` 等在 Phase 外物化。graph 只表达意图。

### D12 放置与拓扑

- **放置**：`LoadTargetPosX/Y` 读目标点，`IsPointInCircle` 判范围，`ClampTargetToRange` 钳制到射程内，`SnapToNearestInCollection` / `SnapToNearestGraphEdge` 吸附到集合成员或导航图边。建造预览、下令合法性、道路吸附都走这里。
- **事件载荷**：`LoadEventPayloadInt/Float` 读事件带来的参数。
- **拓扑判定**：`ControlDomainResolve`（谁是控制域代表）、`ControlDomainControls`（A 能否控制 B）、`KnowledgeHasProjection`（viewer 是否知道目标存在）。这三条是 viewer 相对语义的基础——同一份世界，不同玩家看到不同真相，判定逻辑写在 graph 里而不是散在 UI 代码里。

---

## 5 Showcase 设计原则

在设计覆盖矩阵之前，先定死原则。现有 `gitbook/architecture/graph-ai-showcases.md` 已经确立了一条关键分类，必须延续：

> **Capability showcase 和 Benchmark showcase 是两类入口，不能混。**

- Capability showcase 回答「这个能力怎么用、看得见吗」。画面小、职责单一、不显示性能数字。
- Benchmark showcase 回答「能不能扛规模」。给耗时、分配、丢失率证据，不承担教学职责。

在此之上，针对「覆盖全部 graph 功能」这个目标，补三条原则：

**原则一：一个 showcase 覆盖一个能力域，不贪多。** 一个入口里塞多个无关能力是明确的失败标准。宁可多开入口。

**原则二：能力必须在画面上可见。** graph 内部算了什么，必须外化成玩家能看见的位移、颜色、状态标签、计数。不可见的能力等于没验收。这是现有 showcase 的核心约束：「点必须可见、会动、不能被渲染缓冲静默丢掉」。

**原则三：每个 showcase 配 Gherkin UAT + 覆盖断言。** 场景描述回答「人能看出什么」，覆盖断言回答「哪些 op 真的被执行到了」。后者需要 op 命中计数——没有它，覆盖率是自称的。

---

## 6 Showcase 覆盖矩阵

设计 9 个 capability showcase + 1 个 benchmark field，覆盖 D1-D12 全部能力域。前 4 个已存在，后续为新增设计。

| Showcase | 入口 id | 主覆盖域 | 附带域 | 状态 |
|---|---|---|---|---|
| S1 Level Blueprint | `graph_level_blueprint` | D1 控制流、D6 Blackboard | D10 SendEvent、D12 事件载荷 | ✅ 已有 |
| S2 RTS Stance FSM | `graph_stance_fsm` | D1 分支、D3 比较 | D6 状态存储 | ✅ 已有 |
| S3 Complex BT | `graph_complex_bt` | D1 控制流、D6 任务进度 | D3 数值 | ✅ 已有 |
| S4 50k Stress Field | `graph_stress_field` | 全域规模验收 | — | ✅ 已有 |
| S5 Damage Pipeline | `graph_damage_pipeline` | D4 属性、D6 BB 传值、D7 配置 | D2、D3、D5 | 新增 |
| S6 Targeting Lab | `graph_targeting_lab` | D8 空间查询全管线 | D2、D10 扇出 | 新增 |
| S7 Relationship Web | `graph_relationship_web` | D9 关系图全套 | D8 TargetList、D10 | 新增 |
| S8 Effect Dispatch | `graph_effect_dispatch` | D10 派发七形态 | D7 动态 id、D5 | 新增 |
| S9 Placement & Lifecycle | `graph_placement_lifecycle` | D11 生命周期、D12 放置 | D3 钳制 | 新增 |
| S10 Viewer Topology | `graph_viewer_topology` | D12 控制域/知识投影 | D2 Viewer | 新增 |

### 6.1 反查表：每个能力域被谁覆盖

| 能力域 | 主覆盖 | 交叉验证 |
|---|---|---|
| D1 控制流 | S1, S2, S3 | S5-S10 全部 |
| D2 实体加载 | S5 | S6, S8, S10 |
| D3 数值运算 | S5 | S2, S6, S9 |
| D4 属性读写 | S5 | S6 排序/聚合 |
| D5 Tag 判定 | S8 | S5, S6 过滤 |
| D6 Blackboard | S5 | S1, S2, S3 |
| D7 配置参数 | S5 | S8 动态 id |
| D8 空间查询 | S6 | S7, S8 |
| D9 关系图 | S7 | — |
| D10 派发 | S8 | S6, S7 |
| D11 生命周期 | S9 | — |
| D12 放置拓扑 | S9, S10 | S1 |

D9 和 D11 只有单一覆盖点，是覆盖矩阵里最脆的两格。设计时应在 S7 / S9 内部做多子场景，弥补缺乏交叉验证的风险。

---

## 7 新增 Showcase 详细设计

### S5 Damage Pipeline Lab

**要证明的**：graph 能在 Effect Phase 之间接力完成一次完整的伤害结算，且公式全在数据里。

**场景**：静态靶场。左侧一排攻击者（不同 BaseDamage / 暴击率），右侧一排靶子（不同 Armor / 护盾 / 免疫 Tag）。玩家点选一对组合发起一次攻击。

**画面必须显示的**：每次结算的中间量以浮字形式逐段冒出——`Raw` → `Crit?` → `Mitigated` → `Final`，以及靶子血条的实际下降。三个数字必须能被玩家对上：`Raw - Mitigated = Final`。

**Graph 结构**：

```
OnCalculate:
  E[0]=LoadContextSource, E[1]=LoadContextTarget
  F[0]=LoadAttribute(E[0], BaseDamage)
  F[1]=LoadConfigFloat(ratio)              ; D7 一图多效果
  F[2]=MulFloat(F[0], F[1])
  F[3]=RandomFloat01, F[4]=LoadAttribute(E[0], CritChance)
  B[0]=CompareGtFloat(F[4], F[3])
  JumpIfFalse B[0] → skipCrit
    F[5]=LoadConfigFloat(critMul), F[2]=MulFloat(F[2], F[5])
  skipCrit:
  WriteBlackboardFloat(DamageAmount, F[2])

OnApply Listener (armor, priority=200):
  F[0]=ReadBlackboardFloat(DamageAmount)
  I[0]=ReadBlackboardInt(IsTrueDamage)
  B[0]=CompareEqInt(I[0], 1)
  JumpIfFalse B[0] → mitigate
    WriteBlackboardFloat(FinalDamage, F[0]); Jump → done
  mitigate:
    B[1]=HasTag(E[1], Status.Immune)       ; D5 门控
    ...  armor 公式  →  F[3]
    WriteBlackboardFloat(FinalDamage, F[3])
    WriteBlackboardFloat(MitigatedAmount, SubFloat(F[0],F[3]))
  done:

OnApply Main:
  ModifyAttributeAdd(Health, -ReadBlackboardFloat(FinalDamage))   ; D4 正路
```

**覆盖断言**：`LoadAttribute`、`ModifyAttributeAdd`、`ReadBlackboard*`、`WriteBlackboard*`、`LoadConfigFloat`、`HasTag`、`CompareEqInt`、`CompareGtFloat`、`RandomFloat01`、全部浮点算术、`LoadContext*` 均命中 ≥1 次。

**关键的反面测试**：靶场里必须有一个 `IsTrueDamage=1` 的攻击者和一个持 `Status.Immune` 的靶子，用来证明分支两侧都走过。只走 happy path 的 showcase 不算覆盖。

### S6 Targeting Lab

**要证明的**：空间查询是可组合的管线，每一段过滤/排序/截断都真实生效。

**场景**：一个可平移的「查询器」游标，周围散布不同队伍、不同 Tag、不同血量的静态单位。屏幕上有一排开关，玩家逐个打开管线的每一段。

**画面必须显示的**：命中单位实时高亮。每打开一段过滤器，高亮集合可见地收缩；打开排序后，命中单位上出现序号 1..N；打开 limit 后序号截断在 N。侧栏显示 `AggCount` 和各 `Agg*Attribute` 的实时值。

**子场景（切换形状）**：Radius / Cone / Rectangle / Line 各一档，Hex 地图页额外给 HexRange / HexRing / HexNeighbors 三档。

**覆盖断言**：D8 全部来源 op、全部 filter op、两种 sort、`QueryLimit`、全部 Agg 系列各命中 ≥1 次。`QueryFromCollection` 需要一个预建集合源的开关档。

**为什么这样设计**：空间查询是 graph 里 op 数量最多的一域（约 30 条）。逐个开关的交互形式让「一个入口覆盖多 op」不违反「职责单一」原则——因为职责就是「演示查询管线本身」。

### S7 Relationship Web

**要证明的**：关系图能建边、带度量、可遍历、可聚合，并能驱动效果派发。

**场景**：8-12 个单位摆成环。单位之间的关系边用连线画出，线的粗细 = metric 值，颜色 = flag。玩家可以点两个单位建边/删边，可以给边加仇恨值。

**画面必须显示的**：连线随 metric 实时变粗变细；`QueryOutgoing/Incoming/Mutual` 三种遍历各有一个按钮，点下后被遍历到的节点高亮，且三种结果集可见地不同；聚合面板显示 sum/max/min/avg metric；「对所有仇恨 > 阈值的目标施加标记」按钮触发一次扇出派发，被派发到的节点弹出标记图标。

**Graph 结构要点**：

```
E[0]=LoadCaster
RelationshipQueryOutgoing(E[0], type=Hatred)     → TargetList
RelationshipFilterMetricRange(min=50)            ; 过滤
RelationshipSortByMetric(desc)                   ; 排序
I[0]=RelationshipAggSumMetric                    ; 聚合
E[1]=RelationshipAggMaxEntityByMetric            ; 取最恨的那个
FanOutApplyEffect(Effect.Mark)                   ; 接 D10 扇出
```

**覆盖断言**：D9 全 21 条 op 命中。这是唯一覆盖 D9 的入口，必须做到全覆盖，缺一条就是覆盖矩阵漏洞。

### S8 Effect Dispatch Gallery

**要证明的**：七种派发形态语义确实不同，尤其 `Apply` 与 `Dispatch` 的上下文映射差异。

**场景**：分成 7 个并列小格，每格一个派发形态，同时跑同一批目标，让差异并排可见。

**画面必须显示的**：
- 单体 vs 扇出：命中数量差异。
- 静态 vs 动态 templateId：同一格里目标身上出现不同效果图标（因为 id 来自寄存器）。
- `Apply` vs `Dispatch`：**链式闪电格**是关键 —— 用 `FanOutDispatchEffect` 演示「每跳的 source 是上一跳的 target」，画面上必须画出链的走向箭头，让「上下文映射」这个抽象概念变得可见。
- `RemoveEffectTemplate`：一格专门演示效果被摘掉，图标消失。
- `SendEvent`：发出的事件驱动格子边框闪光，证明 graph → trigger 的接缝通了。

**覆盖断言**：200, 201, 202, 203, 204, 205, 206, 220 全部命中；`LoadConfigEffectId` 命中（动态 id 来源）。

### S9 Placement & Lifecycle Yard

**要证明的**：graph 能做放置合法性判定和吸附，并能表达生命周期意图而不越界改结构。

**场景**：建造场。玩家拖一个建造预览框在地面移动。

**画面必须显示的**：
- 预览框颜色随 `IsPointInCircle` 判定实时切绿/红。
- 拖出射程时预览框被 `ClampTargetToRange` 硬拽回射程边界——玩家能看到「拽不出去」。
- 靠近道路时预览框 `SnapToNearestGraphEdge` 吸附对齐到路网边；靠近集合成员时 `SnapToNearestInCollection` 吸附。
- 确认建造：`BeginLifecycleTransaction` + `InvokeBuiltin(spawn)`，新单位在**下一帧**出现（而非 Phase 内），这个一帧延迟应在 HUD 上标注出来，明确「graph 只排队，系统才物化」的边界。
- 升级消耗：`BeginLifecycleConsumeSource` 让工程车消失、建筑升级，演示消耗语义。

**覆盖断言**：402-408 全部命中，400/401 命中。

### S10 Viewer Topology

**要证明的**：同一份世界在不同 viewer 视角下判定不同，且判定逻辑在 graph 里。

**场景**：分屏。左右两个 viewer（玩家 A / 玩家 B）看同一片战场。中间一排单位，其中部分只被 A 知道、部分只被 B 知道、部分共有。

**画面必须显示的**：
- 同一个单位在左屏可见、右屏是雾（`KnowledgeHasProjection` 判定差异）。
- 点击单位，两屏分别显示「你能否指挥它」（`ControlDomainControls`）——同一目标结论相反。
- 控制域代表用连线指向（`ControlDomainResolve`），演示「代表不是自己」的情况。
- 切换 viewer 按钮，两屏结论互换，证明判定确实是 viewer 相对的而非硬编码。

**覆盖断言**：410, 420, 421, 422 命中，且每条 op 的 true/false 两个分支都有样本。

---

## 8 覆盖度如何被验证

自称覆盖不算覆盖。需要一条机制把「op 是否被执行」变成可断言的数据。

### 8.1 Op 命中计数

在 `GraphExecutor` 挂一个**仅 showcase/测试构建启用**的 op 命中计数器（`ushort op → uint count`）。硬约束：

- 生产构建下必须完全编译掉，不能给热路径加分支。这是 50k benchmark 零分配指标的前提，不能为了统计破坏它。
- 计数器是定长数组，不是字典，避免分配。

### 8.2 覆盖报告

每个 showcase 跑完一轮后导出 `artifacts/graph-coverage/<showcaseId>.json`：

```json
{
  "showcaseId": "graph_targeting_lab",
  "declaredDomains": ["D8"],
  "opHits": { "QueryRadius": 120, "QueryFilterTagAny": 43, "QueryLimit": 43 },
  "declaredOps": ["QueryRadius", "QueryCone", "..."],
  "missingOps": [],
  "unexpectedOps": []
}
```

汇总所有 showcase，与 `GraphOps.cs` 枚举全集比对，产出全局覆盖报告：

```
总 op 数 / 已覆盖 / 未覆盖清单 / 仅单点覆盖清单
```

`missingOps` 非空即 showcase 与声明不符；全局未覆盖清单非空即矩阵有洞。两者都应该是 CI 断言。

### 8.3 分支覆盖比 op 覆盖更重要

一条 `HasTag` 被执行过，不代表它的 false 分支被走过。对含分支语义的 op（`HasTag`、`Compare*`、`JumpIfFalse`、`RelationshipHasFlag`、`ControlDomainControls`、`KnowledgeHasProjection`、`IsPointInCircle`），覆盖标准应是**true/false 双侧各 ≥1 次命中**。

这直接影响场景设计：S5 必须有免疫靶子，S10 必须有两侧结论相反的样本。设计阶段就要把反面样本写进 actor 配置里，不能靠事后补。

---

## 9 实施顺序

按「先补覆盖洞、再补交叉验证」排：

1. **S6 Targeting Lab** —— D8 是 op 最多的域，收益最大。
2. **S7 Relationship Web** —— D9 当前零覆盖，风险最高。
3. **S5 Damage Pipeline** —— 覆盖 D4/D6/D7 三域，且是战斗体系的主线证明。
4. **op 命中计数 + 覆盖报告** —— 前三个做完，验证机制才有足够输入可校准。
5. **S8 Effect Dispatch** —— 依赖 S5 的效果模板资产。
6. **S9 Placement & Lifecycle**、**S10 Viewer Topology** —— 相对独立，可并行。

把计数机制放在第 4 步而不是第 1 步，是因为它的接口形状取决于前几个 showcase 暴露出的真实需求；提前定死容易做成不合用的抽象。

---

## 10 失败标准

沿用现有 showcase 的失败标准，并针对覆盖目标补充。

Capability showcase 失败：玩家看不出实体为什么动；状态反馈和场景动作对不上；一个入口塞了多个无关能力。

Benchmark showcase 失败：实体数不足；点不可见或不动；渲染实例丢失；热路径有分配；Gen0 增长；耗时数据缺失。

覆盖矩阵失败（新增）：

- 声明覆盖某域但 `missingOps` 非空。
- 含分支语义的 op 只有单侧命中。
- 某能力域仅有单点覆盖且该入口内无多子场景。
- 覆盖计数机制泄漏到生产构建、影响热路径分配。

---

## 11 UAT 骨架

```gherkin
Feature: Graph 能力域覆盖

  Scenario: 空间查询管线逐段可见
    Given 玩家启动 Graph Targeting Lab
    When 玩家依次开启关系过滤、Tag 过滤、属性区间过滤、排序、限量
    Then 每开启一段，高亮命中集合都可见地收缩
    And 开启排序后命中单位显示 1..N 序号
    And 侧栏聚合数值与高亮集合一致
    And 覆盖报告中 D8 全部 op 命中且无 missingOps

  Scenario: 关系图驱动效果派发
    Given 玩家启动 Graph Relationship Web
    When 玩家给两个单位之间的仇恨边加值到阈值以上
    And 玩家点击"标记高仇恨目标"
    Then 连线粗细随仇恨值可见变化
    And 仅超过阈值的节点弹出标记图标
    And 覆盖报告中 D9 全部 op 命中

  Scenario: 伤害管线中间量可对账
    Given 玩家启动 Graph Damage Pipeline Lab
    When 玩家让一名攻击者攻击一名有护甲的靶子
    Then 画面依次冒出 Raw、Crit、Mitigated、Final 四段数字
    And Raw 减 Mitigated 等于 Final
    And 靶子血条下降量等于 Final
    When 玩家改用真实伤害攻击者攻击同一靶子
    Then Mitigated 为 0 且 Final 等于 Raw

  Scenario: viewer 相对判定左右不同
    Given 玩家启动 Graph Viewer Topology 分屏
    Then 至少一个单位在左屏可见而在右屏为雾
    And 点击该单位时两屏的可指挥结论相反
    When 玩家交换两侧 viewer
    Then 两屏结论互换
```

---

## 12 与现有文档的关系

- `docs/architecture/interaction/features/_common/graph_ops_reference.md` —— op 级速查表，本篇的能力域归类是它的上层视图。两者应保持同步：新增 op 时同时更新速查表和本篇的能力域表。
- `docs/architecture/gas_combat_infrastructure.md` —— 战斗体系视角，S5 的设计依据。
- `docs/architecture/gas_layered_architecture.md` —— 分层边界，解释为什么 graph 不能做结构变更。
- `gitbook/architecture/graph-ai-showcases.md` —— 现有 4 个入口的职责划分，本篇的 S1-S4 沿用其定义，不重复描述。
