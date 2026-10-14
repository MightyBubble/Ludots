# Mechanism: D6 — Teleport to Point (传送到指定地面点)

> **Examples**: Dota NP Teleport, LoL TF R落点

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Point**
- **Acquisition**: **Explicit**

## Ludots 映射

目标规则图是 `graph.aim.rule.ground_only`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


OrderSubmitter 将点击位置写入 `SpatialBlackboardKey`。

## 实现方案

```
AbilityExecSpec:
  Item[0]: TagClip "channeling" @ tick 0, duration=180 ticks (3s channel)
  Item[1]: EffectSignal @ tick 180 → teleport_effect
    → Phase Graph: 设置 caster position = order_position
```

- Presenter: GroundOverlay(Circle) 在目标点显示传送指示器
- 可选: 添加 "interruptible" tag, 受击打断引导

## 依赖组件

| 组件 | 状态 |
|------|------|
| SpatialBlackboardKey | ✅ 已有 |
| TagClip | ✅ 已有 |
| EffectSignal | ✅ 已有 |
| Position 修改 Graph op | ✅ 已有 |

## 新增需求

无 — 所有依赖已满足。
