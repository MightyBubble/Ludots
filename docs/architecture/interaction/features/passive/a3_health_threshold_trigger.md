# A3 血量阈值触发

周期性 Effect 检测血量比例，低于阈值时自动挂载/移除 buff Tag。

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

挂载一个 PeriodicSearch Effect（period=每10 tick），OnPeriod Phase Graph：

```jsonc
{
  "effect_preset": "PeriodicSearch",
  "period_ticks": 10,
  "phase": "OnPeriod",
  "graph": [
    { "op": 330, "attr": "health_current" },    // LoadSelfAttribute
    { "op": 330, "attr": "health_max" },
    { "op": "DivFloat" },                        // ratio = current / max
    { "op": "CompareGtFloat", "threshold": 0.3 },
    { "op": "IfTrue",  "then": [ { "op": "RemoveTag", "tag": "low_health_buff" } ] },
    { "op": "IfFalse", "then": [ { "op": "AddTag",    "tag": "low_health_buff" } ] }
  ]
}
```

备选方案：使用 `AttributeConstraints` + `AttributeBindingSystem` 在属性变化时触发，避免周期轮询开销。

## 3 依赖组件

| 组件 | 路径 | 状态 |
|------|------|------|
| AttributeBuffer | src/Core/Gameplay/GAS/Components/AttributeBuffer.cs | ✅ 已有 |
| GameplayTagContainer | src/Core/Gameplay/GAS/Components/GameplayTagContainer.cs | ✅ 已有 |
| GraphExecutor | src/Core/NodeLibraries/GASGraph/GraphExecutor.cs | ✅ 已有 |
| Graph ops 330/331 | src/Core/NodeLibraries/GASGraph/GraphOps.cs | ✅ 已有 |
| PeriodicSearch preset | src/Core/Gameplay/GAS/PresetTypeRegistry.cs | ✅ 已有 |

## 4 新增需求

- 无。PeriodicSearch + Graph ops + Tag 组合可完整表达血量阈值触发。

## 5 相关文档

- 架构总览：[../../README.md](../../README.md)
- 用户体验清单 A3：[../../user_experience_checklist.md](../../user_experience_checklist.md)
