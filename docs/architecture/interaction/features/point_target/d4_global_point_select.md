# Mechanism: D4 — Global Point Select (全图点选/小地图可用)

> **Examples**: SC2 Scan, Dota NP Teleport

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Point**
- **Acquisition**: **Explicit**

## Ludots 映射

目标规则图是 `graph.aim.rule.ground_only`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


OrderSubmitter 将点击位置写入 `SpatialBlackboardKey`。

## 实现方案

与 D1 圆形AoE 相同, 唯一差异在于:

1. **Range 限制**: OrderTypeConfig 不设置 range 限制, 或设置为全图范围
2. **小地图**: 指针走 `interaction.context.minimap`，世界点来自 `GraphPointerSampleRuntime`

```
AbilityExecSpec:
  Item[0]: EffectSignal → global_point_effect
    → EffectPresetType: Search
    → Graph: QueryRadius(origin=order_position, radius=250cm, filter=Enemy)
    → FanOutApplyEffect(damage_template)
```

## 依赖组件

| 组件 | 状态 |
|------|------|
| SpatialBlackboardKey | ✅ 已有 |
| Search preset | ✅ 已有 |
| QueryRadius Graph op | ✅ 已有 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Minimap click → world position | P3 | Adapter 层, 非核心 |
