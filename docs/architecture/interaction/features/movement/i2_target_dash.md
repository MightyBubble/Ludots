# Mechanism: I2 目标冲刺 (Target Dash)

> 冲向选定的目标单位

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
  Item[0]: EffectSignal @ tick 0 → target_dash_effect
    → EffectPresetType: Displacement
    → DisplacementDescriptor:
      directionMode: ToTarget
      target: order_target entity
      distanceCm: (可选, 或冲到目标位置)
      durationTicks: 15
```

- 使用已有的 `DisplacementRuntimeSystem`
- 方向动态指向目标单位
- 可配置是否冲到目标位置或停在目标前方

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| InputOrderMapping (Unit) | ✅ 已有 | selectionType: Unit |
| DisplacementPreset | ✅ 已有 | DisplacementRuntimeSystem |
| DisplacementDirectionMode.ToTarget | ✅ 已有 | 朝向目标模式 |
