# Mechanism: I6 击退 (Knockback)

> 将目标单位推离施法者

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Unit**
- **Acquisition**: **Explicit**

## Ludots 映射

目标规则图是 `graph.aim.rule.unit_only`，拾取图是 `graph.aim.pick.hostile`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


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
| `graph.aim.rule.unit_only` | AimInteractionMod | 只打单位 |
| DisplacementPreset | ✅ 已有 | DisplacementRuntimeSystem |
| DisplacementDirectionMode.AwayFromSource | ✅ 已有 | 推离源点模式 |
