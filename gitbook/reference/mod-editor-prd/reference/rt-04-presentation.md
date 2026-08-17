# rt-04 reference · 表现事件

> 现状参考。第一性需求见 [rt-04 PRD](../prd/rt-04-presentation.md)；配置说明见 [rt-04 配置说明](../config/rt-04-presentation.md)。

## 1. 现状快照

- 九种事件：Cast 五种（Started/Failed/Committed/Finished/Interrupted，枚举值 1-5）+ Effect 四种（Applied/Activated/Expired/Cancelled，枚举值 10-13）。
- AbilityCastFailReason 七值：None/OnCooldown/BlockedByTag/NoTarget/InvalidSlot/NotAlive/PreconditionFailed。
- 负载：Kind/Actor/Target/AbilitySlot/AbilityId/EffectTemplateId/AttributeId/Delta/FailReason。
- 缓冲：定容每 tick；容量构造必 >0、满即抛（"overflow is a configuration error"）；事务回滚支持；零分配写路径。
- 容量接线：game.json `presentation.gasPresentationEventCapacity`，核心 mod 基线 65536（`mods/LudotsCoreMod/assets/game.json`）；引擎在装配期构造并注册服务。
- 消费方：GameplayPresentationProjectionSystem（ClearPresentationFlags 组）——投影后清表现标志。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 九种 Kind | src/Core/Gameplay/GAS/Presentation/GasPresentationEventBuffer.cs:9-23 |
| 七值失败原因 | src/Core/Gameplay/GAS/Presentation/GasPresentationEventBuffer.cs:26-37 |
| 事件负载结构 | src/Core/Gameplay/GAS/Presentation/GasPresentationEventBuffer.cs:40-54 |
| 缓冲与溢出合同 | src/Core/Gameplay/GAS/Presentation/GasPresentationEventBuffer.cs:56-104 |
| 容量装配与注册 | src/Core/Engine/GameEngine.cs:1026、1645 |
| 消费系统（ClearPresentationFlags 组） | src/Core/Presentation/Systems/GameplayPresentationProjectionSystem.cs；src/Core/Engine/GameEngine.cs:1861 |
| 容量基线 | mods/LudotsCoreMod/assets/game.json:6 |

**相关文档**：[rt-04 PRD](../prd/rt-04-presentation.md) · [rt-05 reference](rt-05-events.md)
