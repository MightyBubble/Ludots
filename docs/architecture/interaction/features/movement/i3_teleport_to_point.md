# Mechanism: I3 瞬移到点 (Teleport to Point)

> 即时传送到指定地点, 无位移过程

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Point**
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillR"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Point
  isSkillMapping: true
```

## 实现方案

```
AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → teleport_effect
    → Phase Graph:
      1. ReadBlackboardVector2(order_point)
      2. WritePosition(caster, order_point)
    → 或: 新增 Teleport handler (instant position set)
```

- **不使用** DisplacementPreset (无位移过程)
- 直接设置 position 组件
- 可选: 添加传送特效 (VFX)

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| 即时位置设置 (Teleport handler) | P1 | 直接设置位置, 非 displacement |
| Graph op: WritePosition | P1 | 或扩展现有 handler 支持即时传送 |
