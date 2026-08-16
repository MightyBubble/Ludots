# rt-05 reference · 事件总线与帧延迟

> 现状参考。第一性需求见 [rt-05 PRD](../prd/rt-05-events.md)；配置说明见 [rt-05 配置说明](../config/rt-05-events.md)。

## 1. 现状快照

- GameplayEventBus 双缓冲各 4096（=MAX_GAMEPLAY_EVENTS_PER_FRAME）；Publish 写 next、满抛（GAS.GAMEPLAY_EVENT_BUS.ERR.CapacityExceeded）；Update 交换；读只暴露 current 只读视图；写检查点 Capture/Rollback 支持事务回滚。
- 一拍延迟链：AbilityActivation/EffectProcessing 组写 next → 帧末 EventDispatch 组交换+结算丢弃计数 → 下一帧消费者（Reaction/AbilityExec/DeferredTrigger）读 current。组序 AbilityActivation→EffectProcessing→EventDispatch（引擎注册序）。
- 同帧 EventGate 等待者读 current（同帧可见语义，tag-03 同源）。
- ResponseChainTelemetryBuffer：六事件（WindowOpened/PromptRequested/OrderConsumed/ProposalAdded/ProposalResolved/WindowClosed）+ 结果枚举六值（AppliedInstant/CreatedEffect/Cancelled/Negated/TargetDead/TemplateMissing）；容量 4096；溢出静默丢弃+计数（DroppedSinceClear/DroppedTotal）——与表现/诊断/主总线的 fail-fast 不一致（治理项 R3）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 双缓冲与容量 | src/Core/Gameplay/GAS/GameplayEventBus.cs:11-14 |
| 写检查点/回滚 | src/Core/Gameplay/GAS/GameplayEventBus.cs:24-60 |
| Publish 满抛 | src/Core/Gameplay/GAS/GameplayEventBus.cs:62-76 |
| Update 交换与丢弃结算 | src/Core/Gameplay/GAS/GameplayEventBus.cs:78-94 |
| 组序注册 | src/Core/Engine/GameEngine.cs:1743-1858（AbilityActivation/EffectProcessing/EventDispatch） |
| 消费方 | src/Core/Gameplay/GAS/Systems/ReactionSystem.cs；DeferredTriggerProcessSystem.cs（同 tag-03 锚点） |
| 遥测六事件与结果枚举 | src/Core/Gameplay/GAS/ResponseChainTelemetryBuffer.cs:6-31 |
| 遥测容量与溢出计数 | src/Core/Gameplay/GAS/ResponseChainTelemetryBuffer.cs:51-60 |
| 容量常量 | src/Core/Gameplay/GAS/GasConstants.cs（见事实页） |

**相关文档**：[rt-05 PRD](../prd/rt-05-events.md) · [tag-03 reference](tag-03-changed-events.md) · [rt-04 reference](rt-04-presentation.md)
