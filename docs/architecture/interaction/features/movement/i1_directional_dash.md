# Mechanism: I1 方向冲刺 (Directional Dash)

> 朝光标方向快速冲刺固定距离

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Direction**
- **Acquisition**: **Explicit**

## Ludots 映射

目标规则图是 `graph.aim.rule.ground_only`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


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
| `graph.aim.rule.ground_only` | AimInteractionMod | 地面或方向点 |
| DisplacementPreset | ✅ 已有 | DisplacementRuntimeSystem |
| DisplacementDirectionMode.Fixed | ✅ 已有 | 固定方向模式 |
