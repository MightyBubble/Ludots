# Mechanism: D7 — Delayed AoE (延迟AoE/标记位置延迟爆炸)

> **Examples**: Dota Kunkka Torrent, LoL Zilean Q

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
  argsTemplate: { i0: 1 }
```

OrderSubmitter 将点击位置写入 `SpatialBlackboardKey`。

## 实现方案

```
AbilityExecSpec:
  Item[0]: EffectClip @ tick 0, duration=90 ticks (1.5s delay)
    → EffectTemplate: lifetime=90 ticks
    → OnExpire Phase:
      1. QueryRadius(stored_position, radius)
      2. FanOutApplyEffect(delayed_damage)
```

- Performer: GroundOverlay(Circle, growing opacity) 在 1.5s 内渐显
- 位置存储在 EffectTemplate 的 blackboard 或 component 中

## 依赖组件

| 组件 | 状态 |
|------|------|
| SpatialBlackboardKey | ✅ 已有 |
| EffectClip | ✅ 已有 |
| OnExpire Phase | ✅ 已有 |
| QueryRadius Graph op | ✅ 已有 |
| GroundOverlay Performer | ✅ 已有 |

## 新增需求

无 — 所有依赖已满足。
