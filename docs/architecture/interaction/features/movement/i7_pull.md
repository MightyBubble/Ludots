# Mechanism: I7 拉拽 (Pull)

> 将目标单位拉向施法者

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Unit**
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillE"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Unit
  isSkillMapping: true
```

## 实现方案

```
AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → pull_effect
    → EffectPresetType: Displacement
    → DisplacementDescriptor:
      directionMode: TowardSource
      source: caster
      target: order_target
      distanceCm: 500
      durationTicks: 18
```

- 使用已有的 `DisplacementRuntimeSystem`
- `TowardSource` 模式自动计算拉向方向
- 可配合控制效果或追加攻击

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| InputOrderMapping (Unit) | ✅ 已有 | selectionType: Unit |
| DisplacementPreset | ✅ 已有 | DisplacementRuntimeSystem |
| DisplacementDirectionMode.TowardSource | ✅ 已有 | 拉向源点模式 |
