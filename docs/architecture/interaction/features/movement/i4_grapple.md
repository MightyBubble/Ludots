# Mechanism: I4 勾爪/蛛丝 (Grapple Hook)

> 选择环境锚点并快速拉拽自身过去 (Spider-Man/Sekiro)

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Point / Context**
- **Acquisition**: **ContextScored**

## Ludots 映射

目标规则图是 `graph.aim.rule.ground_only`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


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
