# Mechanism: I3 瞬移到点 (Teleport to Point)

> 即时传送到指定地点, 无位移过程

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Point**
- **Acquisition**: **Explicit**

## Ludots 映射

目标规则图是 `graph.aim.rule.ground_only`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


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
