# Mechanism: L1 — Mark Manual Detonate (A技能标记 → B技能手动引爆)

> **Examples**: LoL Zed影标引爆, Tristana E, Dota Skywrath Glaives

## 交互层

交互层与 base ability 相同。标记/引爆全部通过 Tag 实现，无独立交互原语。

- **Skill A (标记)**: Down + Unit, Explicit
- **Skill B (引爆)**: Down + None, Explicit

## Ludots 映射

```
Skill A (标记):
InputOrderMapping:
  actionId: "SkillQ"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Entity
  isSkillMapping: true

Skill B (引爆):
InputOrderMapping:
  actionId: "SkillE"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: None
  isSkillMapping: true
```

## 实现方案

**Skill A — 施加标记**:
```
AbilityExecSpec:
  Item[0]: EffectSignal → apply_mark
    → AddTag("marked", target=enemy, duration=180 ticks)
```

**Skill B — 手动引爆**:
```
AbilityExecSpec:
  Item[0]: EffectSignal → detonate_marks
    → Phase Graph:
      1. QueryRadius(self, 999cm, filter=HasTag("marked"))
      2. FanOutApplyEffect(detonate_damage)
      3. ForEach(tagged_entities): RemoveTag("marked")
```

- 引爆效果伤害可比标记本身更高 (标记的"奖励"交互)
- Skill B 可以有 Precondition: `AnyNearby(HasTag("marked"))`

## 依赖组件

| 组件 | 状态 |
|------|------|
| AddTag handler | ✅ 已有 |
| Tag duration (expiry) | ✅ 已有 |
| QueryRadius Graph op | ✅ 已有 |
| HasTag filter | ✅ 已有 |
| FanOutApplyEffect | ✅ 已有 |
| RemoveTag Graph op | ✅ 已有 |

## 新增需求

无 — 全部可用现有 Tag + Phase Graph 表达。
