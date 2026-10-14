# Mechanism: D7 — Delayed AoE (延迟AoE/标记位置延迟爆炸)

> **Examples**: Dota Kunkka Torrent, LoL Zilean Q

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
  Item[0]: EffectClip @ tick 0, duration=90 ticks (1.5s delay)
    → EffectTemplate: lifetime=90 ticks
    → OnExpire Phase:
      1. QueryRadius(stored_position, radius)
      2. FanOutApplyEffect(delayed_damage)
```

- Presenter: GroundOverlay(Circle, growing opacity) 在 1.5s 内渐显
- 位置存储在 EffectTemplate 的 blackboard 或 component 中

## 依赖组件

| 组件 | 状态 |
|------|------|
| SpatialBlackboardKey | ✅ 已有 |
| EffectClip | ✅ 已有 |
| OnExpire Phase | ✅ 已有 |
| QueryRadius Graph op | ✅ 已有 |
| GroundOverlay Presenter | ✅ 已有 |

## 新增需求

无 — 所有依赖已满足。
