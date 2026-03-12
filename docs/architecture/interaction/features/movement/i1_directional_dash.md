# Mechanism: I1 方向冲刺 (Directional Dash)

> 朝光标方向快速冲刺固定距离

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Direction**
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillE"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Direction
  isSkillMapping: true
```

## 实现方案

```
AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → directional_dash_effect
    → EffectPresetType: Displacement
    → DisplacementDescriptor:
      directionMode: Fixed (from order direction)
      distanceCm: 300
      durationTicks: 10
```

- 使用已有的 `DisplacementRuntimeSystem`
- 方向从 order 的 direction 参数获取
- 固定距离冲刺, 不受目标影响

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| InputOrderMapping (Direction) | ✅ 已有 | selectionType: Direction |
| DisplacementPreset | ✅ 已有 | DisplacementRuntimeSystem |
| DisplacementDirectionMode.Fixed | ✅ 已有 | 固定方向模式 |
