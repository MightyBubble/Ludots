# fx-17 reference · 位移

> 现状参考。第一性需求见 [fx-17 PRD](../prd/fx-17-displacement.md)；配置说明见 [fx-17 配置说明](../config/fx-17-displacement.md)。

## 1. 现状快照

- loader：displacement 块仅 Displacement preset + Instant（反向同错）；directionMode 四值（未知报错列合法值）；非 Fixed 禁 fixedDirectionDeg（Fixed 时必填）；totalDistanceCm/totalDurationTicks 必须 >0；overrideNavigation 必填布尔。
- runtime：HandleApplyDisplacement 解析方向目标（上下文目标实体位置 → EffectTargetPointResolver 保留点 → 施法实例 TargetPos）组装 DisplacementState；同目标已有活跃位移时 TryReplaceActiveDisplacement 就地覆写（保留 PoseWindowRequested、按需撤销 MovementSuppressed2D），否则 CreateDisplacement；注册为 External(Displacement) 独占计划；目标死亡或非正参数静默返回。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 块与 preset 组合校验 | src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs:348-365 |
| displacement 编译 | EffectTemplateLoader.cs:558-604 |
| 位移处理器 | src/Core/Gameplay/GAS/BuiltinHandlers.cs:417-513 |
| 替换实现 | BuiltinHandlers.cs:470-513（TryReplaceActiveDisplacement） |
| External(Displacement) 注册 | BuiltinHandlers.cs:71 |
| 位移状态组件 | src/Core/Gameplay/GAS/Components/DisplacementState.cs |

**相关文档**：[fx-17 PRD](../prd/fx-17-displacement.md) · [fx-17 配置说明](../config/fx-17-displacement.md)
