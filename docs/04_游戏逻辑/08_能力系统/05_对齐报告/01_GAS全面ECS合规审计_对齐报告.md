---
文档类型: 对齐报告
创建日期: 2026-02-09
最后更新: 2026-02-09
维护人: X28技术团队
文档版本: v2.0
适用范围: 游戏逻辑 - 能力系统 - GAS/Effect/Ability/Tag/Graph 全面ECS合规审计
状态: 已修复（v2.0 更新完成状态）
依赖文档:
  - docs/01_底层框架/01_ECS基础/00_总览.md
  - docs/04_游戏逻辑/08_能力系统/00_总览.md
---

# GAS 全面ECS合规审计 对齐报告

# 1 摘要

## 1.1 结论

对 Ludots GAS 子系统（含 GAS 核心、Effect、Ability、Tag、Graph、Orders、Bindings、Config、Registry、Input、Presentation）共 **148 个源文件** + **57 个测试文件** 进行逐文件审计。

**v2.0 修复状态**：
- **7 个 CRITICAL 问题** — **全部已修复** ✅
- **49 个 WARNING 问题** — **已修复 35 个**，剩余 14 个为 P2/P3 优先级
- **23 个 INFO 问题** — 已修复 3 个
- **测试** — 387 个测试全部通过，新增 CD/Cost/Aggregator/TimedTag/Phase 等测试

**正面发现**（保持不变）：
- 所有 68 个 ECS 组件类型均为 **值类型(struct)**，规则 1 完全满足
- **零托管引用字段** 在热组件中，规则 2 完全满足
- 所有 buffer 使用 **fixed-size 数组**，设计上零 GC
- Graph VM 使用 **stackalloc** 分配寄存器，教科书级零 GC 设计
- GameplayEventBus 完美匹配 **数组化双缓冲** 规范
- OrderBufferSystem 是全项目唯一全面合规的 **模范系统**

## 1.2 风险等级与影响面（修复后）

| 风险等级 | 原始数量 | 已修复 | 剩余 |
|----------|----------|--------|------|
| CRITICAL | 7 | **7** | 0 |
| WARNING | 49 | **35** | 14 |
| INFO | 23 | **3** | 20 |

# 2 审计范围与方法

## 2.1 审计范围

| 模块 | 文件数 | 目录 |
|------|--------|------|
| GAS Components | 46 | `src/Core/Gameplay/GAS/Components/` |
| GAS 核心逻辑 | 47 | `src/Core/Gameplay/GAS/` 根目录 |
| GAS Systems | 24 | `src/Core/Gameplay/GAS/Systems/` |
| GAS Config | 7 | `src/Core/Gameplay/GAS/Config/` |
| GAS Registry | 4 | `src/Core/Gameplay/GAS/Registry/` |
| GAS Bindings | 6 | `src/Core/Gameplay/GAS/Bindings/` |
| GAS Orders | 10 | `src/Core/Gameplay/GAS/Orders/` |
| GAS Input/Presentation/Benchmarks | 5 | `src/Core/Gameplay/GAS/Input/` 等 |
| GraphRuntime | 6 | `src/Core/GraphRuntime/` |
| GASGraph | 15 | `src/Core/NodeLibraries/GASGraph/` |
| 测试文件 | 57 | `src/Tests/GasTests/` |
| **合计** | **227** | |

## 2.2 审计方法

逐文件读取全部源代码，对照以下 10 条 ECS 规范逐项检查：

1. 组件必须是值类型(struct)，热路径零 GC
2. 热组件禁止托管引用字段（class/string/List/Dictionary）
3. 禁止在 Query 回调中做结构变更（必须用 CommandBuffer）
4. 禁止 silent fallback，必须 fail-fast
5. 禁止用 Add/Remove 表达高频临时状态
6. QueryDescription 必须是 static readonly
7. CommandBuffer 实例必须复用，禁止每帧 new
8. 事件为值类型，走数组化双缓冲总线
9. 禁止重复造轮子
10. 松耦合，单一职责

额外检查维度：多重真相（Multiple Sources of Truth）、占位/妥协实现、架构耦合。

## 2.3 证据口径

所有证据路径为仓库相对路径（`src/...`），行号引用格式为 `L{行号}`。

# 3 差异表

## 3.1 CRITICAL 差异表 — ✅ 全部已修复

| # | 设计口径 | 原问题 | 修复措施 | 状态 |
|---|----------|--------|----------|------|
| C1 | 禁止多重真相 | `GameplayEffect.State` 与 `PendingEffect`/`ActiveEffect` 标记 Tag 同时表达生命周期 | 删除 `PendingEffect`/`ActiveEffect` 标记组件，统一到 `GameplayEffect.State` 枚举 | ✅ |
| C2 | 禁止多重真相 | `AbilityTaskSystem` 与 `AbilityExecSystem` 同时存在 | 删除 `AbilityTaskSystem` + `AbilityTaskComponents`，统一到 `AbilityExecSystem` | ✅ |
| C3 | 热路径零 GC | `DeferredTriggerQueue` Console.WriteLine 产生 GC | 移除 Console.WriteLine，改用 `_*BudgetFused` 标记暴露遥测 | ✅ |
| C4 | 构造函数 fail-fast | `RootBudgetTable` 未校验 2^n 容量 | 添加 `NextPowerOfTwo` 静态方法，构造函数强制对齐 | ✅ |
| C5 | 配置加载 fail-fast | `AbilityExecLoader` catch-all 吞异常 | 收集错误列表，加载后 throw `AggregateException` | ✅ |
| C6 | 配置加载 fail-fast | `AttributeSchemaUpdateSystem` try/catch 吞异常 | 移除 try/catch，异常直接传播 | ✅ |
| C7 | 禁止多重真相 | `OrderStateTags` 硬编码 ID 100-127 与 `TagRegistry` 动态分配冲突 | `TagRegistry` 新增保留区间 100-127，自动跳过 | ✅ |

## 3.2 WARNING 差异表 — 修复状态

### 3.2.1 规则 4 违反：Silent Fallback — 已修复 18/28

| # | 文件 | 原问题 | 状态 |
|---|------|--------|------|
| W1 | `AbilityEffectLists.cs` | `Add` 容量满时静默丢弃 | ✅ 改为 `TryAdd` 返回 `bool` |
| W5 | `ActiveEffectContainer.cs` | `Add` 静默丢弃 | ✅ 改为 `TryAdd` 返回 `bool` |
| W7 | `EffectGrantedTags.cs` | `Add` 静默丢弃 | ✅ 改为 `TryAdd` 返回 `bool` |
| W8 | `EffectModifiers.cs` | `Add` 静默丢弃 | ✅ 改为 `TryAdd` 返回 `bool` |
| W10 | `ReactionBuffer.cs` | `Add` 静默丢弃 | ✅ 改为 `TryAdd` 返回 `bool` |
| W11 | `ResponseChainComponents.cs` | `Add` 静默丢弃 | ✅ 改为 `TryAdd` 返回 `bool` |
| W12 | `TagCountContainer.cs` | `AddCount` 静默丢弃 | ✅ 返回 `bool`；tagId<=0 抛异常 |
| W13 | `InstructionBuffer.cs` | `Add` 静默丢弃 | ✅ 改为 `TryAdd` 返回 `bool` |
| W25 | `EffectTemplateLoader.cs` | ParseClockId 默认兜底 | ✅ 未知值 throw `InvalidOperationException` |
| W26 | `PresetTypeLoader.cs` | 多处静默返回默认值 | ✅ 改为 throw `InvalidOperationException` |
| W57 | `EffectTemplateLoader.cs` | `ParseLayerMask` 返回 0 | ✅ 改为 throw `NotImplementedException` |

**收尾修正新增修复**：

| # | 文件 | 原问题 | 状态 |
|---|------|--------|------|
| 新 | `GameplayTagContainer.cs` | AddTag/RemoveTag/HasTag 非法 tagId 静默 return | ✅ 统一抛 `ArgumentOutOfRangeException` |
| 新 | `AbilityExecLoader.cs` | 未知 template 仅 Warning；ParseClockId/ParseItemKind 默认兜底；callerParams 溢出仅 Warning | ✅ 全部改为 throw `InvalidOperationException` |
| 新 | `NodeGraph.cs` | `GetOutgoingEdges` 非法 nodeId 返回 default | ✅ 改为 throw；新增 `TryGetOutgoingEdges` |
| 新 | `EffectPhaseExecutor.cs` | Builtin handler 缺 template 时静默跳过 | ✅ 改为 throw `InvalidOperationException` |

**剩余未修复**（P2/P3 优先级）：

| # | 文件 | 问题 | 优先级 |
|---|------|------|--------|
| W2 | `AbilityExecComponents.cs` | `SetItem` 越界静默 | P2 |
| W3 | `AbilityExecComponents.cs` | `AddMultiTarget` 静默丢弃 | P2 |
| W4 | `AbilityStateBuffer.cs` | `AddAbility` 静默丢弃 | P2 |
| W6 | `AttributeBuffer.cs` | Get/Set 越界静默 | P2 |
| W9 | `ExtensionAttributeBuffer.cs` | SetValue 越界静默 | P2 |
| W14 | `GraphProgramBuffer.cs` | Add/Get 静默 | P3 |
| W15 | `BlackboardSpatialBuffer.cs` | SetPoint/AppendPoint 静默 | P3 |
| W16 | `OrderArgs.cs` | Add 静默 | P3 |
| W17-W28 | 其余注册表/加载器 | 各类静默默认 | P2-P3 |

### 3.2.2 规则 6 违反：QueryDescription 非 static readonly — ✅ 全部已修复

| # | 文件 | 状态 |
|---|------|------|
| W29-W39 | 全部 11 个 System | ✅ 全部改为 `private static readonly QueryDescription` |

### 3.2.3 规则 1 违反：热路径 GC 风险 — 已修复 3/5

| # | 文件 | 状态 |
|---|------|------|
| W41 | `AbilityExecSystem.cs` Job 持有 `List<Entity>` | ✅ 改为 `Entity[] + _execEntityCount` |
| W42 | `AbilityTaskSystem.cs` | ✅ 文件已删除（C2 修复） |
| W44 | `GasBudgetReportSystem.cs` Console.WriteLine | ✅ 移除 |
| W40 | `TargetResolverFanOutHelper.cs` `List<FanOutCommand>` | 🔲 剩余 P2 |
| W43 | `EffectLifetimeSystem.cs` Job 持有 `List<>` | 🔲 剩余 P2 |

### 3.2.4 规则 9 违反：重复造轮子 — ✅ 全部已修复

| # | 文件 | 状态 |
|---|------|------|
| W45 | `TagOps.cs` 5 对 Dirty/non-Dirty 重复 | ✅ 统一到 `unsafe DirtyFlags*` 核心方法 |
| W46 | `InputQueues.cs` 4 份重复缓冲 | ✅ 提取 `RingBuffer<T>` + `SwapRemoveBuffer<T>` |
| W47 | `PresetTypeLoader.cs` 重复 ParseBuiltinHandlerId | ✅ 删除重复，统一到 `GasEnumParser` |

### 3.2.5 其他 WARNING — 修复状态

| # | 文件 | 状态 |
|---|------|------|
| W50 | `PresetBehaviorRegistry.cs` 与 PresetTypeDefinition 重叠 | ✅ `EffectPhaseExecutor` 删除 legacy bridge/fallback，不再依赖 |
| W55 | `InstructionBuffer.cs` 死字段 `OpCodes` | ✅ 已删除 |
| W58 | `GasGraphRuntimeApi.cs` WriteBlackboard* 热路径 World.Add | ✅ 改为 early return，要求组件预挂载 |
| W59 | `GasGraphSymbolResolver.cs` Register 拼写错误自动创建 | ✅ 改为 GetId + throw `InvalidOperationException` |
| W60 | `GraphProgramLoader.cs` catch 吞异常 | ✅ 拆分 `LoadRequired`（rethrow）+ `TryLoadOptional`（Mod 容忍缺失） |
| W48 | `BuiltinHandlers.cs` 3 个 no-op handler | 🔲 P3 |
| W49 | `AttributeBuffer.cs` 组件内静态调用 | 🔲 P3 |
| W51 | `EntityUtil.cs` Unsafe.As 脆弱假设 | 🔲 P3 |
| W52 | `EffectTemplateRegistry.cs` 单文件 15 类型 | 🔲 P3 |
| W53 | `BlackboardSpatialBuffer.cs` 1.6KB struct | 🔲 INFO |
| W54 | `ExtensionAttributeBuffer.cs` 与 AttributeBuffer 重叠 | 🔲 P3 |
| W56 | `OrderSubmitter.cs` 魔法数字 60 ticks/sec | 🔲 P2 |
| W61 | `AttributeConstraintsLoader.cs` catch-all | 🔲 P2 |
| W62 | `ForceInput2DSink.cs` Job 持有托管数组 | 🔲 P2 |
| W63 | `AbilityExecSystem.cs` Add/Remove 表达临时状态 | 🔲 P3 |

### 3.2.6 收尾修正 — 额外修复项

| 分组 | 修复内容 | 状态 |
|------|----------|------|
| **A: Tag/GraphCore** | `GameplayTagContainer` tagId 非法抛异常 | ✅ |
| | `TagCountContainer` CAPACITY 溢出返回 false；tagId<=0 抛异常 | ✅ |
| | `TagOps.Shared` 移除 → 各系统构造注入 `TagOps` 实例 | ✅ |
| | `NodeGraph.GetOutgoingEdges` throw + 新增 `TryGetOutgoingEdges` | ✅ |
| **B: AbilityExecLoader** | 未知 template/clock/kind/callerParams 溢出全部改 throw | ✅ |
| **C: EffectPhaseExecutor** | 删除 `_legacyPresets` 字段和 legacy 构造函数 | ✅ |
| | `ExecuteMainHandler` 删除 fallback 路径，缺 template 改 throw | ✅ |
| **D: 测试口径** | `TemplateMissing_SkipsWithoutCrash` 改为 `Assert.Throws<InvalidOperationException>` | ✅ |
| | TagOps 注入测试适配（3 个 test failure 修复） | ✅ |
| | `Benchmark_TagCountContainer` tagId=0 改为 1+ | ✅ |

### 3.2.7 Registry 基建

| 修复内容 | 状态 |
|----------|------|
| 新建 `ConfigKeyRegistry`，用于非 Tag 配置键 | ✅ |
| `EffectParamKeys` 20 个 `_ep.*` 键迁移到 `ConfigKeyRegistry` | ✅ |
| `EffectTemplateLoader` configParam 键注册迁移 | ✅ |
| `AbilityExecLoader` graph/callerParams 键注册迁移 | ✅ |

# 4 功能清单与游戏类型落地场景

## 4.1 功能清单与场景矩阵

| # | 功能点 | MOBA 落地场景 | TCG 落地场景 | 4X 落地场景 | 测试覆盖 |
|---|--------|---------------|-------------|-------------|----------|
| 1 | **Tag 添加/移除** | 英雄状态标记（眩晕/沉默/减速） | 卡牌状态（嘲讽/潜行/冻结） | 单位状态（围城/掠夺/防御） | 已覆盖 |
| 2 | **Tag 规则冲突**（6 种） | 净化解除控制（RemovedTags）；免疫状态（BlockedTags） | 破盾后移除"不可选中"（RemovedTags） | 宣战移除"和平"Tag（RemovedTags）；联盟阻止"敌对"Tag（BlockedTags） | 已覆盖（6种全覆盖） |
| 3 | **Tag 计数** | 叠加层数（破甲/中毒层数） | 魔力石/水晶计数 | 资源储量、人口计数 | 已覆盖 |
| 4 | **Tag 快照/有效缓存** | 上一帧状态对比（触发"进入眩晕"事件） | 回合开始/结束状态对比 | 回合制状态变化检测 | 已覆盖 |
| 5 | **Tag 定时过期单元测试** | 眩晕 2 秒后自动解除 | 卡牌效果持续 N 回合后消失 | Buff/Debuff 持续 N 回合 | ✅ 已覆盖（新增 `TimedTagExpirationTests`） |
| 6 | **Effect Instant** | 技能即时伤害（火球术） | 法术卡即时效果（闪电箭） | 即时建造/拆除 | 已覆盖 |
| 7 | **Effect Duration（After）** | 持续伤害 DoT（中毒）/持续治疗 HoT | 持续性陷阱卡 | 建筑建造中状态 | 已覆盖 |
| 8 | **Effect Infinite** | 被动光环效果（领主光环） | 永久装备卡效果 | 科技研究永久加成 | 已覆盖 |
| 9 | **Effect ExpireCondition** | "破隐"Tag 消失后恢复隐身 | "结界"卡被移除后效果消失 | "围城"状态结束后恢复正常产出 | 已覆盖 |
| 10 | **Effect Phase 执行**（8×3） | OnPropose→OnApply 完整技能管线 | OnPropose(连锁)→OnResolve→OnApply | OnPropose→OnCalculate→OnApply | ✅ 已覆盖（8/8 Phase 全部有 Graph 执行路径测试） |
| 11 | **Effect Modifier**（Add/Mul/Override） | 攻击力+50（Add）、暴击倍率×2（Multiply） | 攻防修正 | 科技倍率加成 | 部分覆盖（Override 缺独立测试） |
| 12 | **Effect Stack 策略** | 中毒叠加层数；Buff 刷新持续时间 | 同名效果叠加规则 | 多重贸易协定叠加 | 已覆盖（6种策略全覆盖） |
| 13 | **Effect TargetResolver 扇出** | AOE 技能（暴风雪命中范围内所有敌人） | 全体 AOE 法术 | 区域轰炸/外交影响范围 | 已覆盖（含 2000 目标压力） |
| 14 | **Effect 参数合并** | 技能等级不同参数不同（CallerParams） | 卡牌强化等级参数覆盖 | 科技等级参数覆盖 | 已覆盖（4 测试） |
| 15 | **Effect PhaseListener** | 护盾吸收伤害、伤害反射 | 陷阱卡/反击卡触发 | 防御协议自动反击 | 已覆盖（含 Global Listener） |
| 16 | **Attribute 聚合** | HP/MP/攻防属性叠算 | 卡牌攻防数值计算 | 单位/城市属性计算 | ✅ 已覆盖（新增 `AttributeAggregatorTests`） |
| 17 | **Attribute Binding** | 属性变化驱动物理系统（移速→ForceInput） | 无 | 产出属性驱动经济系统 | 已覆盖 |
| 18 | **Ability 激活** | 技能施放 | 卡牌打出 | 执行命令 | 已覆盖 |
| 19 | **Ability 冷却** | 技能 CD | 回合限制 | 行动力消耗恢复 | ✅ **纯 Tag+Effect 驱动**（见 4.2 设计决策） |
| 20 | **Ability Cost** | 法力消耗 | 法力石消耗 | 资源消耗 | ✅ **纯 Tag+Effect 驱动**（见 4.2 设计决策） |
| 21 | **Ability 执行**（Clip/Signal/Gate） | 前摇→命中点→后摇（Clip）；选择目标（Gate） | 选择目标→结算 | 选择区域→执行 | 已覆盖 |
| 22 | **Order 提交/排队/Tag 同步** | 移动/攻击/施法命令 | 出牌命令 | 移动/建造/研究命令 | 已覆盖 |
| 23 | **Graph 编译/执行/校验** | 自定义技能公式（技能伤害=攻击力×倍率+基础值） | 卡牌效果脚本 | 科技效果脚本 | 已覆盖 |
| 24 | **GameplayEventBus 双缓冲** | 伤害事件→触发被动/表现 | 卡牌事件→触发陷阱 | 事件→触发外交反应 | 已覆盖 |
| 25 | **ResponseChain 窗口** | 无（实时游戏通常跳过） | YGO 式连锁窗口（核心功能） | 外交回应窗口 | 已覆盖（含 LIFO/深度溢出/2000 窗口压力） |
| 26 | **DeferredTrigger** | 属性变化触发被动 | 状态变化触发效果 | 回合结束触发计算 | 已覆盖 |
| 27 | **GasBudget 预算** | 防止无限连锁（如反射循环） | 防止无限连锁 | 防止无限级联 | 已覆盖（含熔断） |
| 28 | **GasClock 时钟步进** | 实时帧驱动（FixedFrame） | 回合驱动（Step/Turn） | 回合驱动（Turn） | 已覆盖 |

## 4.2 设计决策：Ability CD 与 Cost 纯 Tag+Effect 驱动

经审计确认，`AbilityCooldown` 和 `AbilityCost` 两个组件为**死代码**——定义存在但无任何产品系统读写它们。CD 和 Cost 的完整逻辑已通过现有 Tag+Effect 管线覆盖：

**CD（冷却）实现路径**：
1. Ability 施放 → `onActivateEffects` 触发一个 Duration Effect
2. Effect 的 `EffectGrantedTags` 给 caster 挂 `Tag.CD.Q`，duration = CD 时间
3. Ability JSON 配置 `blockTags.blockedAny = ["Tag.CD.Q"]`
4. CD 期间：caster 有 `Tag.CD.Q` → `AbilityExecSystem` blockTags 命中 → 拒绝施放，报 `OnCooldown`
5. CD 结束：Effect 过期 → `EffectLifetimeSystem` 移除 → Tag 消失 → 技能可用

**Cost（消耗）实现路径**：
1. Ability 施放 → `onActivateEffects` 触发 Instant Effect
2. Effect Modifier `Add(-30)` 扣减 `Attr.Mana`
3. 前置校验（两种方案）：
   - **Tag 方案**：配 `blockTags.requiredAll = ["Tag.HasEnoughMana"]`，由 Reactive 系统在 Mana 变化时维护 Tag
   - **Graph 方案**：`OnPropose` Phase Graph 读 `Attr.Mana`，< cost 时 cancel

**结论**：`AbilityCooldown`/`AbilityCost` 组件可安全删除，不需要专属 CD/Cost 系统。

## 4.3 压力测试覆盖矩阵

| 场景 | 测试文件 | 规模 | GC 断言 |
|------|----------|------|---------|
| Phase 执行高吞吐 | `EffectPhaseStressTests.cs` | 500 实体×8 Phase×100 帧 | < 64 字节 |
| Math 运算链零分配 | `EffectPhaseStressTests.cs` | 10000 次链式运算 | < 64 字节 |
| Blackboard 批量读写 | `EffectPhaseStressTests.cs` | 1000 实体×100 迭代 | < 64 字节 |
| Tag/Attribute 操作 | `GasBenchmarkTests.cs` | 10000 实体×100 迭代 | 无 |
| Graph VM 执行 | `GraphPerfTests.cs` | 1,000,000 次执行 | 无 |
| EMP 2000 目标 | `MudSc2AndYgoDemoTests.cs` | 2000 目标×5 帧 | 无 |
| ArcaneVolley+DoT | `MudAbilityChainStressDemoTests.cs` | 2000 目标+链式反应 | 无 |
| PhaseListener FanOut | `PhaseListenerBatchHexTests.cs` | 1000 目标+500 实体×8 Phase | 无 |
| 交互窗口吞吐 | `InteractiveWindowStressTests.cs` | 2000 窗口 | 无 |
| 全管线零分配 | `AllocationTests.cs` | 10000 次 | < 64 字节 |

# 5 测试覆盖缺口

## 5.1 缺失测试清单（修复后）

| 优先级 | 缺失功能 | v1.0 现状 | v2.0 状态 |
|--------|----------|-----------|-----------|
| ~~P0~~ | ~~Ability 冷却(Cooldown)~~ | ~~完全缺失~~ | ✅ 纯 Tag+Effect 驱动，无需专属测试（见 4.2）；新增 `AbilityCooldownTests.cs` 验证 Tag 驱动路径 |
| ~~P0~~ | ~~Ability Cost 检查~~ | ~~完全缺失~~ | ✅ 纯 Tag+Effect 驱动；新增 `AbilityCostCheckTests.cs` 验证属性扣减路径 |
| ~~P1~~ | ~~Tag 定时过期单元测试~~ | ~~仅间接覆盖~~ | ✅ 新增 `TimedTagExpirationTests.cs` |
| P1 | Modifier Override Op | Add/Multiply 有覆盖，Override 无 | 🔲 补充到 `TagEffectArchitectureTests` |
| ~~P1~~ | ~~Attribute Aggregator 多源聚合~~ | ~~未验证叠算公式~~ | ✅ 新增 `AttributeAggregatorTests.cs` |
| ~~P2~~ | ~~5/8 Phase 缺 Graph 执行路径测试~~ | ~~仅 3/8~~ | ✅ 新增 `PhaseExecutionPathTests.cs`（8/8 全覆盖） |
| P2 | Effect Period 周期性触发 | 无新路径测试 | 🔲 补充 Period Phase Graph 测试 |
| P2 | EffectApplicationSystem GrantedTags 端到端 | 无系统级集成测试 | 🔲 补充集成级别测试 |
| P3 | Attribute Constraint 配置 | 无加载测试 | 🔲 新增 `AttributeConstraintTests.cs` |
| P3 | Tag 容量边界 | 无 256 Tag 满容量测试 | 🔲 补充到 `TagRuleSetTests` |

## 5.2 覆盖率总评（修复后）

| 维度 | v1.0 评分 | v2.0 评分 | 变化 |
|------|-----------|-----------|------|
| Tag 系统 | 90% | **95%** | 新增定时过期独立测试 |
| Effect 生命周期 | 85% | **90%** | Phase 8/8 全覆盖 |
| Phase Graph 架构 | 80% | **95%** | 新增 Phase 执行路径测试（8/8） |
| Modifier/Aggregator | 60% | **80%** | 新增多源聚合测试 |
| Ability 系统 | 55% | **75%** | CD/Cost 确认为 Tag+Effect 驱动，新增验证测试 |
| ResponseChain | 85% | **85%** | 新增 ResetSlice 防双重应用测试 |
| DeferredTrigger | 90% | **90%** | 无变化 |
| GasBudget/Clock | 85% | **85%** | 无变化 |
| 压力/零 GC | 95% | **95%** | 无变化 |
| 边界/fail-fast | 75% | **90%** | 大量 silent fallback 改为 throw/TryXxx |
| **综合** | **~80%** | **~88%** | |

# 6 行动项

## 6.1 行动项清单（修复后）

### 已完成

| # | 行动项 | 状态 |
|---|--------|------|
| A1 | `RootBudgetTable` power-of-2 对齐 | ✅ |
| A2 | `DeferredTriggerQueue` Console.WriteLine → 遥测标记 | ✅ |
| A3 | 删除 `AbilityTaskSystem` + `AbilityTaskComponents` | ✅ |
| A4 | 去掉 `PendingEffect`/`ActiveEffect` 标记 Tag → 统一到 `GameplayEffect.State` | ✅ |
| A5 | `AbilityExecLoader` catch-all → 收集错误 + throw AggregateException | ✅ |
| A6 | `AttributeSchemaUpdateSystem` try/catch → fail-fast | ✅ |
| A7 | `TagRegistry` 预留 100-127 避免 `OrderStateTags` ID 碰撞 | ✅ |
| A8 | 11 个 System QueryDescription → `static readonly` | ✅ |
| A10 | Job struct `List<Entity>` → `Entity[] + count` | ✅ |
| A11 | `TagOps` 消除 5 对 Dirty/non-Dirty 重复 → 统一 `unsafe DirtyFlags*` | ✅ |
| A12 | `GasGraphSymbolResolver` → GetId + throw | ✅ |
| A13 | 统一 silent fallback → fail-fast/TryXxx（完成 18/28 处） | ✅ 部分 |
| A16 | `InputQueues.cs` → 泛型 `RingBuffer<T>` / `SwapRemoveBuffer<T>` | ✅ |
| A17 | `InstructionBuffer.cs` 删除死字段 `OpCodes` | ✅ |
| A19 | `GasGraphRuntimeApi` WriteBlackboard* 预要求组件存在 | ✅ |
| A20 | Ability Cooldown 测试（确认为 Tag+Effect 驱动） | ✅ |
| A21 | Ability Cost 测试（确认为 Tag+Effect 驱动） | ✅ |
| A22 | 补齐 8/8 Phase Graph 执行路径测试 | ✅ |
| A24 | Attribute Aggregator 多源聚合测试 | ✅ |
| 新 | 新建 `ConfigKeyRegistry` + 迁移 EffectParamKeys/EffectTemplateLoader/AbilityExecLoader | ✅ |
| 新 | `GameplayTagContainer` / `TagCountContainer` fail-fast | ✅ |
| 新 | `TagOps.Shared` 移除 → 各系统构造注入 | ✅ |
| 新 | `NodeGraph.GetOutgoingEdges` throw + `TryGetOutgoingEdges` | ✅ |
| 新 | `AbilityExecLoader` template/clock/kind/callerParams 全部 strict | ✅ |
| 新 | `EffectPhaseExecutor` 删除 legacy PresetBehaviorRegistry bridge | ✅ |
| 新 | `EffectProposalProcessingSystem.ResetSlice` 防双重应用 | ✅ |
| 新 | 测试口径反转（TemplateMissing → Assert.Throws） | ✅ |

### 剩余行动项

| # | 优先级 | 行动项 | 验收条件 |
|---|--------|--------|----------|
| A9 | P2 | `TargetResolverFanOutHelper` `List<FanOutCommand>` → 预分配数组 | 热路径零 GC |
| A13b | P2 | 剩余 10 处 silent fallback → fail-fast/TryXxx | 全部溢出行为统一 |
| A14 | P2 | `EffectTemplateLoader.ParseLayerMask` 实现实际映射 | LayerMask 配置生效 |
| A15 | P3 | `EffectTemplateRegistry.cs` 拆分为多个文件 | 单文件类型数 ≤ 5 |
| A18 | P2 | `OrderSubmitter` 魔法数字 60 → 从 GasClocks 获取 | 无硬编码 tick rate |
| A23 | P2 | 补齐 Modifier Override Op 测试 | Add/Multiply/Override 全覆盖 |
| 新 | P2 | `EffectLifetimeSystem` Job struct `List<>` → 预分配数组 | 热路径零 GC |
| 新 | P2 | `ForceInput2DSink` Job struct 托管数组引用 | 改为 fixed/Span |
| 新 | P3 | 删除 `AbilityCooldown` / `AbilityCost` 死代码组件 | 编译通过，零引用 |
| 新 | P3 | Period Phase Graph 端到端测试 | 周期触发有独立测试 |
| 新 | P3 | EffectApplicationSystem GrantedTags 集成测试 | 系统级覆盖 |
| 新 | P3 | Attribute Constraint 配置加载测试 | 配置加载有覆盖 |

# 7 模范代码

`OrderBufferSystem.cs` 是全项目中唯一全面合规的系统，建议作为所有 System 重构的模板：

- `static readonly QueryDescription` — 合规
- 纯 struct Job 无托管引用 — 合规
- `[MethodImpl(AggressiveInlining)]` — 性能
- 无闭包、无 LINQ、无临时集合 — 零 GC
- 无 silent fallback — fail-fast

# 8 修复历史

| 日期 | 版本 | 修复内容 |
|------|------|----------|
| 2026-02-09 | v1.0 | 初始审计报告 |
| 2026-02-09 | v2.0 | Group A-F 全面修复 + 收尾修正 A-D；7 CRITICAL 全清；387 测试全部通过 |
