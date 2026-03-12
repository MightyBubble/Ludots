# Mechanism: I6 击退 (Knockback)

> 将目标单位推离施法者

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
  Item[0]: EffectSignal @ tick 0 → knockback_effect
    → EffectPresetType: Displacement
    → DisplacementDescriptor:
      directionMode: AwayFromSource
      source: caster
      target: order_target
      distanceCm: 400
      durationTicks: 15
```

- 使用已有的 `DisplacementRuntimeSystem`
- `AwayFromSource` 模式自动计算推离方向
- 可配合伤害或眩晕效果

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| InputOrderMapping (Unit) | ✅ 已有 | selectionType: Unit |
| DisplacementPreset | ✅ 已有 | DisplacementRuntimeSystem |
| DisplacementDirectionMode.AwayFromSource | ✅ 已有 | 推离源点模式 |
