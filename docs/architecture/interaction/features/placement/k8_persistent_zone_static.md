# Mechanism: K8 — Persistent Zone Static (放置持续区域效果)

> **Examples**: LoL Morgana W地板, Dota Macropyre, SC2 Psionic Storm持续

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Point**
- **Acquisition**: **Explicit**

## Ludots 映射

目标规则图是 `graph.aim.rule.ground_only`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


## 实现方案

```
AbilityExecSpec:
  Item[0]: EffectSignal → create_zone
    → EffectPresetType: PeriodicSearch
    → anchor: order_point (fixed, 不跟随施法者)
    → period: 30 ticks
    → duration: 300 ticks
    → radius: 200cm
    → filter: Enemy

  OnPeriod Phase Graph:
    1. QueryRadius(origin=anchor, radius=200cm, filter=Enemy)
    2. FanOutApplyEffect(dot_damage_template)
```

**固定锚点**:
- `anchor = order_point` 保存到 Blackboard 并固定
- 区域不跟随施法者移动
- 施法者可自由移动

**视觉表现**:
```
Presenter: GroundOverlay(Circle, radius=200cm, color=purple, opacity=0.5)
  lifetime: 300 ticks
  position: anchor
```

## 依赖组件

| 组件 | 状态 |
|------|------|
| PeriodicSearch preset | ✅ 已有 |
| Fixed anchor position | ✅ 已有 |
| QueryRadius Graph op | ✅ 已有 |
| FanOutApplyEffect | ✅ 已有 |
| Duration expiration | ✅ 已有 |
| GroundOverlay Presenter | ✅ 已有 |

## 新增需求

无 — 所有依赖已满足。PeriodicSearch 使用固定 `order_point` 作为 origin 即可。
