# A6 光环

PeriodicSearch Effect 周期性 SpatialQuery 找周围单位，FanOutApplyEffect 挂载短期 buff（Lifetime 略大于 period 防止断档）。

## 1 交互层

| 维度 | 值 |
|------|-----|
| InputConfig | 无 (Passive 不需要 InputBinding) |
| TargetMode | None |
| Acquisition | N/A |

## 2 Ludots 实现

### InputOrderMapping 配置

```jsonc
// 无 InputOrderMapping — 由 PeriodicSearch Effect 的 OnPeriod Phase Graph 驱动
```

### AbilityExecSpec 配置

Entity 挂载 `PeriodicSearch` Effect（period=每30 tick, radius=300cm）：

```jsonc
{
  "effect_preset": "PeriodicSearch",
  "period_ticks": 30,
  "search_radius_cm": 300,
  "target_filter": "Ally",
  "phase": "OnPeriod",
  "graph": [
    { "op": "SpatialQuery", "radius": 300, "filter": "Ally" },
    { "op": "FanOutApplyEffect", "effect_id": "aura_buff" }
  ]
}
```

`aura_buff` 配置（Lifetime=40 ticks，略大于 period 确保不断档）：

```jsonc
{
  "effect_preset": "None",
  "lifetime": { "kind": "Duration", "ticks": 40 },
  "same_type_policy": "Replace",
  "modifiers": [
    { "attribute": "armor", "op": "Add", "value": 5 }
  ]
}
```

**无新代码**，PeriodicSearch preset + `BuiltinHandlers.HandleSpatialQuery` 已实现。

## 3 依赖组件

| 组件 | 路径 | 状态 |
|------|------|------|
| PeriodicSearch preset | src/Core/Gameplay/GAS/PresetTypeRegistry.cs | ✅ 已有 |
| BuiltinHandlers.HandleSpatialQuery | src/Core/Gameplay/GAS/Handlers/BuiltinHandlers.cs | ✅ 已有 |
| EffectLifetimeSystem | src/Core/Gameplay/GAS/Systems/EffectLifetimeSystem.cs | ✅ 已有 |
| SpatialQueryService | src/Core/Gameplay/Spatial/SpatialQueryService.cs | ✅ 已有 |

## 4 新增需求

- 无。PeriodicSearch + SpatialQuery + FanOutApplyEffect 完整覆盖光环场景。

## 5 相关文档

- 架构总览：[../../README.md](../../README.md)
- 用户体验清单 A6：[../../user_experience_checklist.md](../../user_experience_checklist.md)
