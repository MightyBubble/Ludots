# Mechanism: K8 — Persistent Zone Static (放置持续区域效果)

> **Examples**: LoL Morgana W地板, Dota Macropyre, SC2 Psionic Storm持续

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
  Item[0]: EffectSignal → create_zone
    → EffectPresetType: PeriodicSearch
    → anchor: order_point (fixed, 不跟随施法者)
    → period: 30 ticks
    → duration: 300 ticks
    → radius: 200cm
    → filter: Enemy

  OnPeriod Phase Graph:
    1. QueryRadius(origin=anchor, radius=200cm, filter=Enemy)
    2. FanOutApplyEffect(dot_damage_template)
```

**固定锚点**:
- `anchor = order_point` 保存到 Blackboard 并固定
- 区域不跟随施法者移动
- 施法者可自由移动

**视觉表现**:
```
Performer: GroundOverlay(Circle, radius=200cm, color=purple, opacity=0.5)
  lifetime: 300 ticks
  position: anchor
```

## 依赖组件

| 组件 | 状态 |
|------|------|
| PeriodicSearch preset | ✅ 已有 |
| Fixed anchor position | ✅ 已有 |
| QueryRadius Graph op | ✅ 已有 |
| FanOutApplyEffect | ✅ 已有 |
| Duration expiration | ✅ 已有 |
| GroundOverlay Performer | ✅ 已有 |

## 新增需求

无 — 所有依赖已满足。PeriodicSearch 使用固定 `order_point` 作为 origin 即可。
