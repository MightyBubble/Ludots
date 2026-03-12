# Mechanism: L5 — Debuff Follow-up (Debuff→后续技能增强)

> **Examples**: GoW冰冻后碎裂伤害, Dota各种debuff互动, LoL Lissandra冻结

## 交互层

交互层与 base ability 相同。后续技能增强通过 Tag precondition + conditional Phase Graph 实现。

- **Skill A (施加Debuff)**: Down + Unit/Direction, Explicit
- **Skill B (增强后续)**: Down + Unit, Explicit (检查目标 Tag)

## Ludots 映射

```
Skill A (施加冰冻/debuff):
InputOrderMapping:
  actionId: "SkillQ"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Direction
  isSkillMapping: true

Skill B (碎裂/增强后续):
InputOrderMapping:
  actionId: "SkillR"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Entity
  isSkillMapping: true
```

## 实现方案

**Skill A — 施加 Debuff Tag**:
```
AbilityExecSpec:
  Item[0]: EffectSignal → apply_frost
    → AddTag("frosted", target=hit_enemy, duration=120 ticks)
    → ModifyAttribute(target, move_speed, multiply=0.3)
```

**Skill B — 检查 Debuff 并增强**:
```
AbilityExecSpec:
  Item[0]: EffectSignal → shatter_hit
    → Phase Graph:
      if target HasTag("frosted"):
        → damage *= 1.5               // 增强伤害倍率
        → RemoveTag("frosted")
        → AddTag("shattered", duration=60 ticks)
        → ApplyEffect(shatter_explosion, radius=100cm)  // 范围爆炸
      else:
        → ApplyEffect(base_damage)    // 普通伤害
```

**增强读取方式**:
- Phase Graph 中用 `HasTag(target, "frosted")` 做分支
- 不修改交互层, 仅 Effect 层条件判断

## 依赖组件

| 组件 | 状态 |
|------|------|
| AddTag / RemoveTag | ✅ 已有 |
| Tag duration (expiry) | ✅ 已有 |
| HasTag conditional branch | ✅ 已有 |
| Conditional damage multiplier | ✅ 已有 |
| FanOutApplyEffect (爆炸) | ✅ 已有 |

## 新增需求

无 — 全部可用现有 Tag + Phase Graph 条件分支表达。
