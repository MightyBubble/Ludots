# Mechanism: D4 — Global Point Select (全图点选/小地图可用)

> **Examples**: SC2 Scan, Dota NP Teleport

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Point**
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillW"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Position
  // 唯一差别: Range 不做限制, 或 Range=Global
  // 小地图点击: 需要 InputOrderMappingSystem 支持 minimap click → world position 转换
```

OrderSubmitter 将点击位置写入 `SpatialBlackboardKey`。

## 实现方案

与 D1 圆形AoE 相同, 唯一差异在于:

1. **Range 限制**: OrderTypeConfig 不设置 range 限制, 或设置为全图范围
2. **小地图支持**: InputOrderMappingSystem 需要支持 minimap click → world position 转换

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
