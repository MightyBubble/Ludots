# A8 连击叠层被动

与 A2 相同机制，但 stack 来源是连击而非普攻，每次技能命中 OnHit Effect 递增 stack_count Attribute，N 层后 proc effect 并重置。

## 1 交互层

| 维度 | 值 |
|------|-----|
| InputConfig | 无 (Passive 不需要 InputBinding) |
| TargetMode | None |
| Acquisition | N/A |

## 2 Ludots 实现

### InputOrderMapping 配置

```jsonc
// 无 InputOrderMapping — 由连击技能命中时的 ApplyEffectTemplate(stack_counter) 驱动
```

### AbilityExecSpec 配置

每次连击技能命中 → `ApplyEffectTemplate(stack_counter)`，stack_counter Effect 的 OnApply Phase Graph：

```jsonc
{
  "phase": "OnApply",
  "graph": [
    { "op": 330, "attr": "combo_stack_count" },       // LoadSelfAttribute
    { "op": "AddInt", "value": 1 },
    { "op": 331, "attr": "combo_stack_count" },       // WriteSelfAttribute
    { "op": "CompareEqInt", "attr": "combo_stack_count", "threshold": 4 },
    { "op": "IfTrue", "then": [
        { "op": "ApplyEffectTemplate", "id": "lethal_tempo_proc" },
        { "op": 331, "attr": "combo_stack_count", "value": 0 }  // 重置
    ]}
  ]
}
```

与 A2 完全相同的实现机制，区别仅在于：
- A2: 普攻命中触发
- A8: 连击技能命中触发

依赖 `GraphExecutor` + `GasGraphRuntimeApi.ModifyAttributeAdd()`，**无新代码**。

## 3 依赖组件

| 组件 | 路径 | 状态 |
|------|------|------|
| AttributeBuffer | src/Core/Gameplay/GAS/Components/AttributeBuffer.cs | ✅ 已有 |
| GameplayTagContainer | src/Core/Gameplay/GAS/Components/GameplayTagContainer.cs | ✅ 已有 |
| GraphExecutor | src/Core/NodeLibraries/GASGraph/GraphExecutor.cs | ✅ 已有 |
| Graph ops 330/331 | src/Core/NodeLibraries/GASGraph/GraphOps.cs | ✅ 已有 |
| EffectPhaseExecutor | src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs | ✅ 已有 |

## 4 新增需求

- 无。与 A2 共享相同基础设施，Graph ops (330/331) + EffectPhaseExecutor 完整覆盖连击叠层被动。

## 5 相关文档

- 架构总览：[../../README.md](../../README.md)
- 用户体验清单 A8：[../../user_experience_checklist.md](../../user_experience_checklist.md)
