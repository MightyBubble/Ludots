# Mechanism: M1 — Stationary Channel (站桩引导, 可被打断)

> **Examples**: Dota CM大招, LoL Katarina R, SC2 Nuke, WoW法术引导

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **None** (或 Direction)
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillR"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: None
  isSkillMapping: true
```

## 实现方案

**引导技能**:
```
AbilityExecSpec:
  Item[0]: TagClip "channeling" @ tick 0, duration=180 ticks
  Item[1]: TagClip "rooted" @ tick 0, duration=180 ticks  // 不能移动
  Item[2-N]: EffectSignal @ periodic ticks (每 30 ticks)
    → ApplyEffect(channel_damage_tick)
```

**CC 打断机制**:
```
Stun/Silence effect 的 Phase Graph:
  if target HasTag("channeling"):
    → RemoveTag("channeling")
    → RemoveTag("rooted")
    → AbilityExecSystem 检测到 channeling tag 丢失
      → InterruptAny → Finish (中断当前技能)
```

**手动取消**:
```
玩家按 ESC / 移动键:
  → RemoveTag("channeling")
  → 同样触发 InterruptAny
```

## 依赖组件

| 组件 | 状态 |
|------|------|
| TagClip | ✅ 已有 |
| channeling tag | ✅ 已有 |
| rooted tag | ✅ 已有 |
| PeriodicEffect | ✅ 已有 |
| InterruptAny on tag loss | ✅ 已有 |
| CC effect RemoveTag | ✅ 已有 |

## 新增需求

无 — 全部依赖已满足。InterruptAny 机制已在 AbilityExecSystem 中实现。
