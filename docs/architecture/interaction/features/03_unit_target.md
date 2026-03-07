# Feature: Unit Target Abilities (C1–C9)

> 清单覆盖: C1 敌方, C2 友方, C3 任意, C4 死亡友方, C5 地形物, C6 召唤物, C7 窃取技能, C8 传送到目标, C9 牵引连接

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Unit**
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillQ"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Entity
  isSkillMapping: true
  argsTemplate: { i0: 0 }  // ability slot
```

### InteractionMode 变体:

| Mode | 行为 |
|------|------|
| TargetFirst | 先选单位, 按键即施放 |
| SmartCast | 按键时取光标下单位, 立即施放 |
| AimCast | 按键进入选取模式, 点击确认 |
| SmartCastWithIndicator | 按住显示范围, 松开施放 |

所有模式已在 `InputOrderMappingSystem` 中实现。

## 实现方案

### C1: 敌方单位 — 伤害/控制

```
AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → targeted_damage
    → EffectTemplate:
      preset: InstantDamage
      target: order_target (from EntityBlackboardKey)
      modifiers: [{ attr: health, op: Add, value: -200 }]
      configParams: [{ key: "damage", value: 200 }]
```

**TargetFilter**: 在 Order 提交前由 `InputOrderMappingSystem` 检查, 或在 Effect 的 `TargetFilter` 配置中指定 `Hostile`

### C2: 友方单位 — 治疗/增益

```
EffectTemplate:
  preset: Heal
  targetFilter: Friendly
  modifiers: [{ attr: health, op: Add, value: +150 }]
```

### C3: 任意单位(效果不同)

```
EffectTemplate:
  preset: None (custom graph)
  Phase Graph:
    1. LoadContextTarget()
    2. GetRelationship(caster_team, target_team)
    3. CompareEqInt(relationship, Hostile)
    4. if hostile → ApplyEffectTemplate(polymorph)
    5. if friendly → ApplyEffectTemplate(haste_buff)
```

- 已有 `GetRelationship()` 在 `IGraphRuntimeApi`

### C4: 死亡友方 — 复活

```
需求: TargetFilter 需支持 "DeadAlly" 过滤
实现:
  1. 死亡单位保留 Entity (添加 "dead" tag, 不立即销毁)
  2. SelectionRule 新增: filter=DeadFriendly
  3. Ability Phase Graph:
     a. 检查 target HasTag("dead")
     b. RemoveTag("dead"), SetAttribute(health, base_health * 0.5)
```

- **需要**: `SelectionRuleRegistry` 新增 DeadFriendly filter, 或 SelectionRule.RelationshipFilter 扩展

### C5: 地形/可破坏物

```
需求: SelectionRule 支持 EntityLayer filter
实现:
  1. 树木/石头等设为特定 EntityLayer (Destructible)
  2. SelectionRule: filter=Destructible layer
  3. Phase Graph 处理: 对可破坏物施加效果 (HP减为0等)
```

- 已有: `QueryFilterLayer` Graph op (op 115)

### C6: 操控自己的召唤物

```
实现:
  1. 召唤物有 "summoned" tag + owner entity ref (Blackboard)
  2. SelectionRule: filter=OwnedSummon (custom)
  3. Phase Graph: 通过 Blackboard 给召唤物写新目标/命令
```

### C7: 窃取技能

```
实现:
  1. Target Unit (enemy)
  2. Phase Graph:
     a. 读取 target 最近使用的 ability ID (需要 LastUsedAbility attribute 或 tag)
     b. 将该 ability 注册到 caster 的 AbilityStateBuffer
     c. 替换特定 slot 的 ability
```

- **需要**: `LastUsedAbilityId` 记录机制 + 动态修改 AbilityStateBuffer 的 Graph op

### C8: 传送到目标身边

```
AbilityExecSpec:
  Item[0]: EffectSignal → teleport_to_target
    → Phase Graph:
      1. LoadContextTarget() → E[1]
      2. 读取 E[1].Position
      3. 设置 E[0].Position = E[1].Position + offset
```

### C9: 牵引/连接

```
实现:
  1. EffectClip (duration=tether_duration)
  2. GrantedTags on caster: "tethered"
  3. GrantedTags on target: "tethered_target"
  4. Tether Effect 的 OnPeriod Graph:
     a. 计算 caster-target 距离
     b. if 距离 > break_range → RemoveTag, 销毁 effect
     c. else → ApplyEffectTemplate(heal/damage per tick)
```

- 已有: Periodic effect + Graph 计算距离

## 依赖组件

| 组件 | 状态 |
|------|------|
| InputOrderMapping.Entity | ✅ 已有 |
| InteractionModeType (4种) | ✅ 已有 |
| AutoTargetPolicy | ✅ 已有 (NearestInRange, NearestEnemyInRange) |
| SelectionRuleRegistry | ✅ 已有 |
| GetRelationship() | ✅ 已有 |
| QueryFilterLayer | ✅ 已有 |

## 新增需求

| 需求 | 优先级 | 清单项 |
|------|--------|--------|
| SelectionRule 支持 DeadFriendly filter | P1 | C4 |
| LastUsedAbilityId 记录 + 动态 ability slot 写入 | P2 | C7 |
| Tether 组件 (持续距离监测 + 断裂) | P1 | C9 |
