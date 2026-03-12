# Mechanism: K4 — AI Summon (召唤AI自动单位)

> **Examples**: LoL Malzahar虫, Dota NP树人, LoL Yorick小鬼

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Point**
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillW"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Position
  isSkillMapping: true
```

## 实现方案

```
AbilityExecSpec:
  Item[0]: EffectSignal → create_ai_summon
    → EffectPresetType: CreateUnit
    → BuiltinHandler: CreateUnit
    → UnitCreationDescriptor:
      templateId: "treant_summon"
      position: order_point
      ownerId: caster
      lifetime: 600 ticks
      controllable: false
      aiGraphId: "aggressive_melee_ai"
      tags: ["summon", "ai_controlled"]
      attributes:
        health: 500
        move_speed: 250
        attack_damage: 40
```

**AI行为**:
```
AI Graph "aggressive_melee_ai":
  1. QueryRadius(self, 800cm, filter=Enemy)
  2. SelectClosest()
  3. If target found:
       → MoveToTarget(target)
       → If in range: AutoAttack(target)
  4. Else:
       → FollowOwner(caster)
```

## 依赖组件

| 组件 | 状态 |
|------|------|
| CreateUnit handler | ✅ 已有 |
| UnitCreationDescriptor | ✅ 已有 |
| AI Graph system | ⚠️ 需扩展 |
| Auto-attack system | ✅ 已有 |
| Follow behavior | ⚠️ 需扩展 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| AI Graph integration | P1 | 绑定AI行为图 |
| Follow owner behavior | P2 | 无目标时跟随主人 |
| Lifetime expiration | P1 | 超时自动销毁 |
| Multiple summons | P2 | 支持同时多个召唤物 |
