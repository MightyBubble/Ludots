# A5 死亡触发

Entity 监听 entity_death EventTag，Chain 挂载复活 Effect 或由 TriggerManager 脚本处理特殊逻辑。

## 1 交互层

| 维度 | 值 |
|------|-----|
| InputConfig | 无 (Passive 不需要 InputBinding) |
| TargetMode | None |
| Acquisition | N/A |

## 2 Ludots 实现

### InputOrderMapping 配置

```jsonc
// 无 InputOrderMapping — 由 ResponseChainListener 监听 entity_death EventTag 驱动
```

### AbilityExecSpec 配置

Entity 挂载 `ResponseChainListener`，监听 `entity_death` EventTag，`ResponseType = Chain`：

```jsonc
{
  "response_chain_listener": {
    "event_tag": "entity_death",
    "response_type": "Chain",
    "chain_effect_id": "resurrection_effect"
  }
}
```

`resurrection_effect` 配置（以 Anivia 蛋/Aegis 复活为例）：

```jsonc
{
  "effect_preset": "None",
  "lifetime": { "kind": "Duration", "ticks": 300 },
  "modifiers": [
    { "attribute": "health_current", "op": "Set", "value": 1 }
  ],
  "tags_to_add": [ "invulnerable", "egg_form" ]
}
```

备选方案（复杂脚本逻辑）：

```jsonc
// GameplayEvent("on_death") → TriggerManager.FireEvent() → Mod 脚本处理
```

## 3 依赖组件

| 组件 | 路径 | 状态 |
|------|------|------|
| ResponseChainListener | src/Core/Gameplay/GAS/Components/ResponseChainComponents.cs | ✅ 已有 |
| ResponseChain 系统 | src/Core/Gameplay/GAS/Systems/ResponseChainSystem.cs | ✅ 已有 |
| TriggerManager | src/Core/Scripting/TriggerManager.cs | ✅ 已有 |
| GameplayTagContainer | src/Core/Gameplay/GAS/Components/GameplayTagContainer.cs | ✅ 已有 |

## 4 新增需求

- 无。ResponseChain Chain 类型 + TriggerManager 脚本覆盖全部死亡触发场景。

## 5 相关文档

- 架构总览：[../../README.md](../../README.md)
- 用户体验清单 A5：[../../user_experience_checklist.md](../../user_experience_checklist.md)
