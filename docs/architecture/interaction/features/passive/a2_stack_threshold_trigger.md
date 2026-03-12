# A2 条件叠层触发

每次普攻命中追加 stack，Phase Graph 检测叠层到阈值后自动触发 proc effect 并重置。

## 1 交互层

| 维度 | 值 |
|------|-----|
| InputConfig | 无 (Passive 不需要 InputBinding) |
| TargetMode | None |
| Acquisition | N/A |

## 2 Ludots 实现

### InputOrderMapping 配置

```jsonc
// 无 InputOrderMapping — 由普攻命中时的 ApplyEffectTemplate(stack_counter) 驱动
```

### AbilityExecSpec 配置

每次普攻命中 → `ApplyEffectTemplate(stack_counter)`，stack_counter Effect 的 OnApply Phase Graph：

```jsonc
{
  "phase": "OnApply",
  "graph": [
    { "op": 330, "attr": "stack_count" },       // LoadSelfAttribute
    { "op": "AddInt", "value": 1 },
    { "op": 331, "attr": "stack_count" },       // WriteSelfAttribute
    { "op": "CompareEqInt", "attr": "stack_count", "threshold": 3 },
    { "op": "IfTrue", "then": [
        { "op": "ApplyEffectTemplate", "id": "proc_effect" },
        { "op": 331, "attr": "stack_count", "value": 0 }  // 重置
    ]}
  ]
}
```

依赖 `GraphExecutor` + `GasGraphRuntimeApi.ModifyAttributeAdd()`，**无新代码**，Graph ops `LoadSelfAttribute`(330) 和 `WriteSelfAttribute`(331) 已支持。

## 3 依赖组件

| 组件 | 路径 | 状态 |
|------|------|------|
| AttributeBuffer | src/Core/Gameplay/GAS/Components/AttributeBuffer.cs | ✅ 已有 |
| GameplayTagContainer | src/Core/Gameplay/GAS/Components/GameplayTagContainer.cs | ✅ 已有 |
| GraphExecutor | src/Core/NodeLibraries/GASGraph/GraphExecutor.cs | ✅ 已有 |
| Graph ops 330/331 | src/Core/NodeLibraries/GASGraph/GraphOps.cs | ✅ 已有 |
| EffectPhaseExecutor | src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs | ✅ 已有 |

## 4 新增需求

- 无。现有 Graph ops (330/331) + EffectPhaseExecutor 可完整表达叠层到阈值触发逻辑。

## 5 相关文档

- 架构总览：[../../README.md](../../README.md)
- 用户体验清单 A2：[../../user_experience_checklist.md](../../user_experience_checklist.md)
