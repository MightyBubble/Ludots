# GAS 战斗体系基建与 MOBA 实践指南

本篇讲清楚两件事：

*   GAS 现有基建如何支撑完整的 MOBA 战斗体系（伤害管线、CC、护盾、自动攻击、资源系统）。
*   哪些能力尚在计划中（属性派生、位移效果），以及当前的临时方案。

核心原则：**一切战斗逻辑通过 Effect + Graph + Tag 组合实现，不硬编码公式。**

## 1 战斗体系总览

GAS 提供 8 个基建能力，覆盖所有战斗场景：

*   **Effect 生命周期**：OnPropose → OnCalculate → OnResolve → OnHit → OnApply → OnPeriod → OnExpire → OnRemove，每个 Phase 可挂载自定义逻辑。
*   **Phase Listener**：监听特定 Effect Tag/TemplateId 的某个 Phase，在 Pre/Main/Post 之后统一分发（目标→来源→全局，按 priority 排序）。
*   **Blackboard**：per-entity 临时键值存储（float/int/Entity），用于 Phase 之间传递中间计算结果（如 DamageAmount、MitigatedAmount）。
*   **Graph VM**：指令式小程序，在 Effect Phase 内执行，支持读写属性、读写 Blackboard、条件分支、空间查询、效果分发。
*   **Tag 系统**：256-bit 定容位集，支持 HasTag/ContainsAll/Intersects；TagCountContainer 支持堆叠层数。
*   **属性聚合**：Reset→Apply Modifiers(Add/Mul/Override)→标脏→Sink 落地。
*   **Sink**：跨层数据边界，集中处理类型转换（float↔Fix64）、写入策略、时钟域解耦。
*   **Preset Type**：内建效果类型（InstantDamage/DoT/HoT/Buff/Search/PeriodicSearch/LaunchProjectile/CreateUnit/ApplyForce2D），每种 Preset 预绑定默认 Phase Handler。

参考：

*   Effect 生命周期 Phase：`src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs`
*   Phase 四段式（Pre→Main→Post→Listeners）：`src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` (L10-L24)
*   Graph VM 指令集：`src/Core/NodeLibraries/GASGraph/GraphOps.cs`
*   Tag 容器：`src/Core/Gameplay/GAS/Components/GameplayTagContainer.cs`
*   属性聚合：`src/Core/Gameplay/GAS/Systems/AttributeAggregatorSystem.cs`
*   Preset 枚举：`src/Core/Gameplay/GAS/EffectTemplateRegistry.cs`

## 2 伤害管线（Damage Pipeline）

伤害管线完全由现有基建支撑，无需核心改动。

### 2.1 标准化 Blackboard Key 约定

| Key 名称 | 类型 | 写入方 | 读取方 |
|----------|------|--------|--------|
| DamageAmount | float | OnCalculate Graph | OnApply Listener（减伤） |
| DamageType | int | OnCalculate Graph | OnApply Listener（分类） |
| IsTrueDamage | int | OnCalculate Graph | OnApply Listener（跳过减伤） |
| FinalDamage | float | OnApply Listener（减伤后） | OnApply Main（扣血） |
| MitigatedAmount | float | OnApply Listener | 护盾/统计系统 |

### 2.2 完整流程

```
1. OnCalculate Graph
   - LoadContextSource E[0], LoadContextTarget E[1]
   - LoadAttribute(E[0], BaseDamage) → F[0]
   - 读配置系数、计算公式
   - WriteBlackboardFloat(E[effect], DamageAmount, F[result])

2. OnApply Listener（护甲减伤，priority=200，scope=Target）
   - ReadBlackboardFloat(E[effect], DamageAmount) → F[0]
   - ReadBlackboardInt(E[effect], IsTrueDamage) → I[0]
   - 如果 IsTrueDamage=0：
     - LoadAttribute(E[target], Armor) → F[1]
     - 减伤公式：FinalDamage = DamageAmount × 100/(100+Armor)
   - 否则：FinalDamage = DamageAmount
   - WriteBlackboardFloat(E[effect], FinalDamage, F[result])
   - WriteBlackboardFloat(E[effect], MitigatedAmount, ...)

3. OnApply Main（扣血）
   - ReadBlackboardFloat(E[effect], FinalDamage) → F[0]
   - ModifyAttributeAdd(Source, Target, Health, -F[0])

4. OnApply Listener（吸血/反甲回调，priority=50，scope=Source）
   - ReadBlackboardFloat(E[effect], FinalDamage) → F[0]
   - 按吸血比例回复 Source 的 Health
```

### 2.3 SkipMainFlags 实现 True Damage

OnCalculate Graph 可通过设置 SkipMain flag 跳过 OnApply 的默认减伤 Listener。但更推荐的做法是用 Blackboard 的 `IsTrueDamage` flag 让减伤 Listener 自行跳过，保持 Main 不变。

### 2.4 EffectContext 约定

*   `E[0]` / `EffectContext.Source`：施法者
*   `E[1]` / `EffectContext.Target`：受击者
*   `E[2]` / `EffectContext.TargetContext`：附加上下文（AOE 中心、链式效果原始目标等）

参考：

*   Phase 执行器：`src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs`
*   Listener 缓冲与匹配：`src/Core/Gameplay/GAS/Components/EffectPhaseListenerBuffer.cs`
*   Graph 指令 Handler：`src/Core/NodeLibraries/GASGraph/GasGraphOpHandlerTable.cs`
*   Blackboard 存储：`src/Core/Gameplay/GAS/Components/BlackboardFloatBuffer.cs`

## 3 CC（控制效果）体系

CC 全部通过 Tag + GrantedTags 实现，不需要专用系统。

### 3.1 标准 CC Tag 约定

| Tag | 用途 |
|-----|------|
| Status.Stunned | 眩晕：阻止移动 + 施法 + 自动攻击 |
| Status.Rooted | 定身：阻止移动，允许施法 |
| Status.Silenced | 沉默：阻止施法，允许移动 + 自动攻击 |
| Status.KnockedUp | 击飞：阻止一切操作（配合位移效果） |
| Status.Disarmed | 缴械：阻止自动攻击 |
| Status.Slowed | 减速：由移动系统读取 |

### 3.2 效果配置示例

```json
{
  "id": "Effect.Debuff.Stun",
  "presetType": "Buff",
  "lifetime": "After",
  "duration": { "durationTicks": 60 },
  "grantedTags": [
    { "tag": "Status.Stunned", "formula": "Fixed", "amount": 1 }
  ]
}
```

效果存活期间，目标身上持有 `Status.Stunned` Tag；效果过期时自动移除。

### 3.3 AbilityActivationBlockTags 门控

能力模板可声明激活条件：

*   `RequiredAll`：激活时必须持有的 Tag 集（全部满足才放行）
*   `BlockedAny`：激活时如果持有任一 Tag 则拒绝（如被沉默时不能施法）
*   `InterruptAny`：施法中如果获得任一 Tag 则打断当前施法

检查逻辑：

```
if (!actorTags.ContainsAll(RequiredAll)) → 激活失败
if (actorTags.Intersects(BlockedAny))   → 激活失败
if (actorTags.Intersects(InterruptAny)) → 打断施法
```

参考：

*   Tag 门控检查：`src/Core/Gameplay/GAS/Systems/AbilityExecSystem.cs`
*   GameplayTagContainer 操作：`src/Core/Gameplay/GAS/Components/GameplayTagContainer.cs`

### 3.4 TagCount 堆叠

`TagCountContainer` 支持同一 Tag 的多层堆叠（最大 65535 层，16 个不同 Tag）：

*   减速层数：每层减速 10%，3 层 = 30%
*   易伤层数：每层增伤 5%

```
AddCount(Status.Slowed, 1)   → 当前 2 层
RemoveCount(Status.Slowed, 1) → 当前 1 层
GetCount(Status.Slowed)       → 返回层数
```

参考：`src/Core/Gameplay/GAS/Components/TagCountContainer.cs`

## 4 护盾系统

护盾通过 Phase Listener 模式实现：Shield Effect 监听 Damage Effect 的 OnApply 阶段。

### 4.1 实现方式

1.  创建 Shield Buff Effect，挂载 Phase Listener：
    *   `listenTag = 0`（匹配所有伤害效果，或指定具体 Tag）
    *   `phase = OnApply`
    *   `scope = Target`（当持有者是受击方时触发）
    *   `priority = 300`（在减伤 Listener 之后执行，但在扣血 Main 之前）

2.  Listener 的 Graph 程序：
    *   读取 BB.FinalDamage
    *   读取 Shield 属性当前值
    *   计算吸收量 = min(Shield, FinalDamage)
    *   修改 BB.FinalDamage -= 吸收量
    *   ModifyAttributeAdd(target, Shield, -吸收量)
    *   如果 Shield ≤ 0，移除护盾效果

### 4.2 配置示例

```json
{
  "id": "Effect.Buff.Shield",
  "presetType": "Buff",
  "lifetime": "After",
  "duration": { "durationTicks": 600 },
  "modifiers": [{ "attribute": "ShieldAmount", "op": "Add", "value": 200.0 }],
  "phaseListeners": [
    {
      "listenTag": "",
      "phase": "onApply",
      "scope": "target",
      "action": "graph",
      "graphProgram": "Graph.Shield.Absorb",
      "priority": 300
    }
  ]
}
```

## 5 自动攻击与 On-Hit

### 5.1 自动攻击定位

自动攻击是 AI/Order 层驱动的循环行为，不是 GAS 核心功能：

*   AI 系统检测攻击目标（距离、仇恨、优先级）
*   按攻速间隔发布 CastAbility Order
*   Ability 发布 EffectRequest（AutoAttack Effect，带 `Effect.AutoAttack` Tag）

### 5.2 On-Hit 效果挂载

On-Hit 效果通过 Phase Listener 实现：

*   `listenTag = Effect.AutoAttack`
*   `phase = OnApply`
*   `scope = Source`（当持有者是攻击方时触发）
*   Listener Graph 可执行额外效果（附加魔法伤害、触发被动等）

### 5.3 攻速与间隔

攻速属性由 AI 层读取，决定 CastAbility Order 的间隔 tick 数：

```
AttackInterval = BaseAttackInterval / (1 + AttackSpeed / 100)
```

此公式由 AI 系统实现，不属于 GAS 核心。

## 6 资源系统（Mana/Energy/Fury）

### 6.1 回复

用 Attribute + 持续周期 Effect（HoT-like）实现自然回复：

```json
{
  "id": "Effect.Passive.ManaRegen",
  "presetType": "HoT",
  "lifetime": "Infinite",
  "duration": { "periodTicks": 20 },
  "modifiers": [{ "attribute": "Mana", "op": "Add", "value": 2.0 }]
}
```

### 6.2 衰减

用 Tag 条件 Effect 实现脱战后资源衰减（如 Fury 递减）：

*   Tag 条件：当实体持有 `State.OutOfCombat` Tag 时激活衰减效果
*   周期性扣除 Fury 属性

### 6.3 能力消耗

能力消耗通过 Effect 扣除 + Tag 门控激活实现：

1.  能力的 OnApply 效果包含 `ModifyAttributeAdd(Mana, -Cost)`
2.  激活前检查 `Mana >= Cost`（通过 Graph 或 Blackboard 条件）
3.  不使用硬编码 CostCheck 组件（已废弃并删除）

## 7 属性派生（Derived Attributes）— [计划中]

> **状态**：计划中，尚未实现。

### 7.1 现状

当前 `AttributeAggregatorSystem` 仅支持三种扁平操作：

*   `Add`：基础值 + 增量
*   `Multiply`：基础值 × 系数
*   `Override`：直接覆盖

无法表达：

*   `Ability Haste → CD Multiplier = 1/(1+AH/100)`（非线性）
*   `Armor → Physical EHP = HP × (1+Armor/100)`（属性间依赖）

### 7.2 目标设计

在 AttributeAggregatorSystem 的 Apply Modifiers 之后、标脏之前，内嵌执行 Derived Graph：

```
1. Reset Current = Base
2. Apply Active Effect Modifiers (Add/Mul/Override)  ← 现有
3. [新增] 执行 AttributeDerivedGraphBinding 的 Graph 程序
4. Mark Dirty Attributes                              ← 现有
```

Graph 程序通过 `LoadSelfAttribute` / `WriteSelfAttribute` 读写当前实体属性，可表达任意非线性公式。

参考：`src/Core/Gameplay/GAS/Systems/AttributeAggregatorSystem.cs`

## 8 位移效果（Displacement）— [计划中]

> **状态**：计划中，尚未实现。

### 8.1 现状

当前 `ForceInput2DSink` 仅做力注入（每帧加力），不具备位移语义：

*   无法覆盖导航输入（位移期间玩家仍可操作）
*   无法按固定距离/时长驱动位移
*   无法中断位移

### 8.2 目标设计

新增 `Displacement` Preset Effect Type：

*   **覆盖导航**：位移期间接管移动控制
*   **持续时间**：按 tick 驱动（dash 20 ticks，knockback 15 ticks）
*   **方向模式**：ToTarget（冲刺）/ AwayFromSource（击退）/ TowardSource（拉拽）/ Fixed（闪现）
*   **可中断**：特定 Tag（如 Cleanse）可提前终止
*   **全程 Fix64**：确定性数学

参考：`src/Core/Gameplay/GAS/Bindings/ForceInput2DSink.cs`

## 9 约束清单

*   **不允许硬编码伤害公式** → 用 Graph 程序计算，通过 Blackboard 传递中间结果。
*   **不允许硬编码 CostCheck** → 用 Tag 门控 + Effect 扣除（`AbilityCost` 组件已废弃删除）。
*   **不允许 query 内结构变更** → 用 `CommandBuffer` 或延迟列表，query 外回放。
*   **不允许 float 参与 gameplay state** → 用 `Fix64` / `Fix64Vec2` 保证确定性。
*   **不允许 Phase 之间隐式依赖执行顺序** → Phase Listener 按 priority 排序；系统按 SystemGroup 固化。
*   **Listener Buffer 预算**：单个实体最多 32 个 Phase Listener，超出时 TryAdd 返回 false。
*   **TagCount 预算**：单个实体最多 16 种 Tag 堆叠。

参考：

*   ECS 开发实践：`docs/developer-guide/01_ecs_soa_principles.md`
*   GAS 分层架构：`docs/developer-guide/11_gas_layered_architecture.md`
*   Trigger 开发指南：`docs/developer-guide/08_trigger_guide.md`
