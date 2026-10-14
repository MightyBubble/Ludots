# Mechanism: D8 — Persistent Zone (持续区域/放下后持续生效)

> **Examples**: LoL Morgana W, Dota Keeper Illuminate

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
  Item[0]: EffectClip @ tick 0
    → EffectPresetType: PeriodicSearch
    → Period: 30 ticks
    → Radius: 200cm
    → Position: stored from order (不跟随施法者)
    → OnPeriod: FanOutApplyEffect(dot_tick)
    → Duration: 300 ticks
```

- Presenter: GroundOverlay(Circle, radius, pulsing alpha) 持续显示直到区域消失

## 依赖组件

| 组件 | 状态 |
|------|------|
| SpatialBlackboardKey | ✅ 已有 |
| PeriodicSearch preset | ✅ 已有 |
| EffectClip | ✅ 已有 |
| FanOutApplyEffect | ✅ 已有 |
| GroundOverlay Presenter | ✅ 已有 |

## 新增需求

无 — 所有依赖已满足。
