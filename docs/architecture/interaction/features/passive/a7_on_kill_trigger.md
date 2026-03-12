# A7 击杀触发

Killer 的 ResponseChainListener 监听 entity_killed EventTag，Chain 挂载重置冷却/返还资源 Effect，或由 EventGate 等待 kill_confirmed tag。

## 1 交互层

| 维度 | 值 |
|------|-----|
| InputConfig | 无 (Passive 不需要 InputBinding) |
| TargetMode | None |
| Acquisition | N/A |

## 2 Ludots 实现

### InputOrderMapping 配置

```jsonc
// 无 InputOrderMapping — 由 ResponseChainListener 监听 entity_killed EventTag 驱动
```

### AbilityExecSpec 配置

Killer Entity 挂载 `ResponseChainListener`，监听 `entity_killed` EventTag，`ResponseType = Chain`：

```jsonc
{
  "response_chain_listener": {
    "event_tag": "entity_killed",
    "response_type": "Chain",
    "chain_effect_id": "reset_cooldown_effect"
  }
}
```

`reset_cooldown_effect` 配置（以 Darius 大招重置为例）：

```jsonc
{
  "phase": "OnApply",
  "graph": [
    { "op": 331, "attr": "ability_r_cooldown", "value": 0 }  // WriteSelfAttribute 重置冷却
  ]
}
```

备选方案（AbilityExecSpec EventGate）：

```jsonc
{
  "ability_exec_spec": {
    "gates": [
      { "kind": "EventGate", "wait_for_tag": "kill_confirmed" }
    ]
  }
}
```

击杀时系统发 `PresentationEvent(Kind=EntityKilled, Source=killer)`，Killer 的 ResponseChainListener 触发。

## 3 依赖组件

| 组件 | 路径 | 状态 |
|------|------|------|
| ResponseChainListener | src/Core/Gameplay/GAS/Components/ResponseChainComponents.cs | ✅ 已有 |
| ResponseChain 系统 | src/Core/Gameplay/GAS/Systems/ResponseChainSystem.cs | ✅ 已有 |
| EventGate | src/Core/Gameplay/GAS/Components/AbilityExecSpec.cs | ✅ 已有 |
| GraphExecutor | src/Core/NodeLibraries/GASGraph/GraphExecutor.cs | ✅ 已有 |

## 4 新增需求

- 无。ResponseChain Chain 类型 + EventGate 完整覆盖击杀触发场景。

## 5 相关文档

- 架构总览：[../../README.md](../../README.md)
- 用户体验清单 A7：[../../user_experience_checklist.md](../../user_experience_checklist.md)
