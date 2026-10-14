# Mechanism: D2 — Ring AoE (环形/甜甜圈区域)

> **Examples**: LoL Diana R边缘效果, GoW符文攻击

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Point**
- **Acquisition**: **Explicit**

## Ludots 映射

目标规则图是 `graph.aim.rule.ground_only`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


OrderSubmitter 将点击位置写入 `SpatialBlackboardKey`。

## 实现方案

```
Phase Graph:
  1. QueryRadius(origin, outer_radius)
  2. QueryFilterNotRadius(inner_radius)  // 排除内圈
  → FanOutApplyEffect(ring_damage)
```

- **需要**: `QueryFilterNotRadius` 或 `QueryRing` Graph op (排除内圆)
- **替代**: 用两次 QueryRadius 然后 list 差集
- Presenter: `GroundOverlay` (Ring, inner_radius, outer_radius)

## 依赖组件

| 组件 | 状态 |
|------|------|
| SpatialBlackboardKey | ✅ 已有 |
| QueryRadius Graph op | ✅ 已有 |
| GroundOverlay Presenter | ✅ 已有 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| QueryRing / QueryFilterNotRadius op | P2 | 排除内圆的空间查询 |
