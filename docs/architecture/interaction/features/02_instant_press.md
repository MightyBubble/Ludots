# Feature: Instant Press Abilities (B1–B9)

> 清单覆盖: B1 自我buff, B2 自身AoE, B3 全图, B4 闪烁, B5 时间回溯, B6 召回武器, B7 净化, B8 自爆, B9 嘲讽

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **None**
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillQ"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: None
  isSkillMapping: true
```

所有 B 类技能共享同一交互配置, 差异全在 Effect 层。

## 实现方案

### B1: 自我buff

```
AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → self_buff_effect
    → EffectTemplate: Buff preset
    → Modifiers: [{ attr: attack_damage, op: Multiply, value: 1.5 }]
    → Duration: 300 ticks
    → GrantedTags: ["empowered"]
```

### B2: 以自身为中心AoE

```
AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → aoe_search_effect
    → EffectPresetType: Search
    → BuiltinHandler: SpatialQuery (radius=500cm, filter=Enemy)
    → OnHit: FanOutApplyEffect(stun_effect)
```

- `HandleSpatialQuery` + `HandleDispatchPayload` 已有

### B3: 全图即时

```
AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → global_damage_effect
    → Phase Graph:
      1. QueryRadius(origin=caster, radius=999999)  // 全图
      2. QueryFilterRelationship(Hostile)
      3. FanOutApplyEffect(damage_template)
```

- 或: 用 PeriodicSearch 配超大半径, 但 Search preset 更直接

### B4: 闪烁/短距瞬移

```
AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → blink_effect
    → Phase Graph:
      1. ReadBlackboardEntity(target_pos)  // 如有, 否则用自身面朝方向
      2. WriteBlackboardFloat(pos_x, new_x)
      3. WriteBlackboardFloat(pos_y, new_y)
    → 或: BuiltinHandler: ApplyForce(instant teleport mode)
```

- **注意**: 需要确认 `ApplyForce` handler 是否支持瞬移语义, 或需新增 `Teleport` handler

### B5: 时间回溯

```
实现:
  1. 挂载一个 Permanent 的 PeriodicEffect (period=1 tick)
     → 每 tick 记录当前位置到 ring buffer (Blackboard 或专用组件)
  2. 激活 Recall 技能时:
     → EffectSignal → recall_effect
     → Phase Graph: 读取 N ticks 前的位置 → 设置当前位置
     → GrantedTags: ["invulnerable", duration=0.5s]
```

- **可能需要新增**: 位置历史记录组件 (PositionHistory ring buffer)
- 或用 Blackboard 存最近 N 帧位置 (但 Blackboard slot 有限)

### B6: 召回武器

```
实现:
  1. 投出斧头时 → CreateUnit(axe_entity, position=target_point)
  2. 召回时:
     → EffectSignal → recall_axe_effect
     → Phase Graph:
       a. 找到 axe_entity (HasTag: thrown_axe, owner=caster)
       b. 设置 axe 的 DisplacementState 朝向 caster
       c. axe 的 PeriodicSearch 沿途造成伤害
```

- 已有: `DisplacementRuntimeSystem`, `CreateUnit` handler

### B7: 净化

```
AbilityExecSpec:
  Item[0]: EffectSignal → cleanse_effect
    → Phase Graph:
      1. 遍历 target 的 GameplayTagContainer
      2. 移除所有带 "cc" 前缀的 tag
      3. 或: 移除所有 EffectTemplate 带 "debuff" tag 的 active effects
```

- **可能需要**: Graph op 来批量移除匹配某 pattern 的 tag/effect

### B8: 自爆/B9: 嘲讽

- B8: Search(self AoE) → 大量伤害 + 自身 HP=0
- B9: Search(self AoE, filter=Enemy) → FanOutApplyEffect(taunt_tag, duration)

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| InputOrderMapping (None) | ✅ 已有 | selectionType: None |
| AbilityExecSpec | ✅ 已有 | EffectSignal item |
| Search preset | ✅ 已有 | SpatialQuery + DispatchPayload |
| ApplyForce handler | ✅ 已有 | 力/位移 |
| DisplacementRuntime | ✅ 已有 | 用于召回 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Teleport handler 或 ApplyForce instant 模式 | P1 | B4 闪烁需要 |
| PositionHistory 组件 | P2 | B5 时间回溯需要位置历史 |
| 批量 Tag/Effect 清除 Graph op | P2 | B7 净化需要 |
