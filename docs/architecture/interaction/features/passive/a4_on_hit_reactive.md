# A4 受击自动触发

Entity 挂载 ResponseChainListener 监听伤害事件，Chain 回调读取入射伤害并按比例反弹。

## 1 交互层

| 维度 | 值 |
|------|-----|
| InputConfig | 无 (Passive 不需要 InputBinding) |
| TargetMode | None |
| Acquisition | N/A |

## 2 Ludots 实现

### InputOrderMapping 配置

```jsonc
// 无 InputOrderMapping — 由 ResponseChainListener 监听 damage_applied EventTag 驱动
```

### AbilityExecSpec 配置

Entity 挂载 `ResponseChainListener`，监听 `damage_applied` EventTag，`ResponseType = Chain`：

```jsonc
{
  "response_chain_listener": {
    "event_tag": "damage_applied",
    "response_type": "Chain",
    "chain_effect_id": "return_damage_effect"
  }
}
```

`return_damage_effect` 的 Phase Graph 读取入射伤害值并按比例返还给来源：

```jsonc
{
  "phase": "OnApply",
  "graph": [
    { "op": "LoadContextAttribute", "attr": "incoming_damage" },
    { "op": "MulFloat", "value": 0.3 },           // 30% 反弹比例
    { "op": "ApplyDamageToSource" }
  ]
}
```

**完美匹配**现有 ResponseChain 架构，**无新代码**。

## 3 依赖组件

| 组件 | 路径 | 状态 |
|------|------|------|
| ResponseChainListener | src/Core/Gameplay/GAS/Components/ResponseChainComponents.cs | ✅ 已有 |
| ResponseChain 系统 | src/Core/Gameplay/GAS/Systems/ResponseChainSystem.cs | ✅ 已有 |
| GraphExecutor | src/Core/NodeLibraries/GASGraph/GraphExecutor.cs | ✅ 已有 |

## 4 新增需求

- 无。现有 ResponseChain + Chain 类型完整覆盖受击自动触发场景。

## 5 相关文档

- 架构总览：[../../README.md](../../README.md)
- 用户体验清单 A4：[../../user_experience_checklist.md](../../user_experience_checklist.md)
