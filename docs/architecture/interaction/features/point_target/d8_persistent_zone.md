# Mechanism: D8 — Persistent Zone (持续区域/放下后持续生效)

> **Examples**: LoL Morgana W, Dota Keeper Illuminate

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
  Item[0]: EffectClip @ tick 0
    → EffectPresetType: PeriodicSearch
    → Period: 30 ticks
    → Radius: 200cm
    → Position: stored from order (不跟随施法者)
    → OnPeriod: FanOutApplyEffect(dot_tick)
    → Duration: 300 ticks
```

- Performer: GroundOverlay(Circle, radius, pulsing alpha) 持续显示直到区域消失

## 依赖组件

| 组件 | 状态 |
|------|------|
| SpatialBlackboardKey | ✅ 已有 |
| PeriodicSearch preset | ✅ 已有 |
| EffectClip | ✅ 已有 |
| FanOutApplyEffect | ✅ 已有 |
| GroundOverlay Performer | ✅ 已有 |

## 新增需求

无 — 所有依赖已满足。
