# Mechanism: I2 目标冲刺 (Target Dash)

> 冲向选定的目标单位

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Unit**
- **Acquisition**: **Explicit**

## Ludots 映射

目标规则图是 `graph.aim.rule.unit_only`，拾取图是 `graph.aim.pick.hostile`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


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
| `graph.aim.rule.unit_only` | AimInteractionMod | 只打单位 |
| DisplacementPreset | ✅ 已有 | DisplacementRuntimeSystem |
| DisplacementDirectionMode.ToTarget | ✅ 已有 | 朝向目标模式 |
