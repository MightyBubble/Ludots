# Mechanism: I4 勾爪/蛛丝 (Grapple Hook)

> 选择环境锚点并快速拉拽自身过去 (Spider-Man/Sekiro)

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Point / Context**
- **Acquisition**: **ContextScored**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillE"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: ContextScored
  isSkillMapping: true

ContextScored 配置:
  候选锚点: 环境中有 "grapple_point" tag 的实体
  Precondition: grapple_point in view cone + distance < range
  Score: angle_score + distance_score
```

## 实现方案

```
AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → grapple_effect
    → EffectPresetType: Displacement
    → DisplacementDescriptor:
      directionMode: ToTarget
      target: selected_grapple_point
      distanceCm: (到达锚点位置)
      durationTicks: 20
```

- 使用 ContextScored 自动选择最佳锚点
- 锚点实体需要 "grapple_point" tag
- 执行时使用 Displacement(ToTarget) 拉拽自身

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| ContextScored selection | ✅ 已有 | 自动选择最佳目标 |
| DisplacementPreset | ✅ 已有 | DisplacementRuntimeSystem |
| GameplayTag system | ✅ 已有 | 标记 grapple_point |
