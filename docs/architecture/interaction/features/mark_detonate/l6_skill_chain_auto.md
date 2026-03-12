# Mechanism: L6 — Skill Chain Auto (技能链: A命中后自动触发B)

> **Examples**: Dota连招(先晕后打), LoL自动combo, Sekiro连击

## 交互层

交互层与 base ability 相同。自动触发后续效果通过 ResponseChain 实现。

- **Skill A**: Down + Unit/Direction, Explicit (正常施法)
- **Skill B**: 自动触发 (无玩家输入)

## Ludots 映射

```
Skill A (触发技能):
InputOrderMapping:
  actionId: "SkillQ"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Direction
  isSkillMapping: true
```

## 实现方案

**Skill A — 命中后自动触发 Skill B**:
```
AbilityExecSpec (Skill A):
  Item[0]: EffectSignal → projectile_hit
    → OnHit ResponseChainListener:
      eventTagId: on_hit
      responseType: Chain
        → ApplyEffectTemplate(follow_up_effect)
```

**Follow-up Effect (Skill B)**:
```
EffectTemplate "follow_up_effect":
  Phase Graph:
    1. ReadBlackboard(hit_target)
    2. ApplyEffect(stun_effect, target=hit_target, duration=60 ticks)
    3. Delay(30 ticks)
    4. ApplyEffect(damage_effect, target=hit_target)
```

**连锁变体** (多段自动):
```
ResponseChainListener (on Skill A hit):
  → Chain: ApplyEffect(B)
    → B OnHit: Chain: ApplyEffect(C)
      → C OnHit: Chain: ApplyEffect(D)
```

## 依赖组件

| 组件 | 状态 |
|------|------|
| ResponseChainListener | ✅ 已有 |
| on_hit event | ✅ 已有 |
| Chain responseType | ✅ 已有 |
| ApplyEffectTemplate | ✅ 已有 |
| Delay Graph op | ✅ 已有 |
| Blackboard target read | ✅ 已有 |

## 新增需求

无 — 全部可用现有 ResponseChain + Phase Graph 表达。
