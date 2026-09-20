# ab-02 reference · 执行时间轴

> 现状参考。第一性需求见 [ab-02 PRD](../prd/ab-02-exec-timeline.md)；配置说明见 [ab-02 配置说明](../config/ab-02-exec-timeline.md)。

## 1. 现状快照

- ExecItemKind 11 种 + None：Clip 类 EffectClip=1/TagClip=2/TagClipTarget=3；Signal 类 EffectSignal=10/EventSignal=11/TagSignal=13/TagSignalTarget=14（编号跳过 12）；Gate 类 InputGate=20/EventGate=21/TargetCollectionGate=22；End=255。
- TagSignal/TagSignalTarget 的增/删语义在 payloadA 整数（0=加 1=删），JSON 无枚举名。
- ExecEffectDispatchTarget 四值：Default/Source/Target/TargetContext（dispatchTarget 编进 payloadA）。
- 运行状态六值：Running/GateWaiting/Committed/Finished/Interrupted/Failed；Committed=2 全库无赋值点。
- 运行实例字段：状态、OrderId、StartAbsoluteTick、ActiveClockId、NextItemIndex、Target、TargetContext、WaitRequestId、WaitTagId、GateDeadline、TerminalFailureReason 等全表见组件文件。
- 起播：黑板键 Cast_SlotIndex=110（缺失→MissingBlackboardSlot）、Cast_TargetEntity=111、Cast_TargetPosition=112（多点：首点原点、末点目标）；rescan 同帧上限 4 轮。
- 推进：CurrentTick=ClockNow−StartAbsoluteTick；到期效果先过容量预检（队列不足→SubmissionQueueFull）；打断命中 interruptAny→Interrupted；订单替换→仅发 CastInterrupted 并移除。
- 终态：Finished→Completed、Interrupted→Cancelled、Failed→Failed；reason=None 抛 TerminalReasonMissing。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| ExecItemKind/状态/SoA 结构 | src/Core/Gameplay/GAS/Components/AbilityExecComponents.cs:10-47、54-60、65-142 |
| AbilityExecInstance 字段 | AbilityExecComponents.cs:187-224 |
| 起播 Phase 1（黑板/多点） | src/Core/Gameplay/GAS/Systems/AbilityExecSystem.cs:144-391（黑板 :173-184、:300-322） |
| 推进 Phase 2 | AbilityExecSystem.cs:395-610（打断 :510-517、订单替换 :427-439） |
| 条目消费 AdvanceItems | AbilityExecSystem.cs:867-999 |
| 效果容量预检 | AbilityExecSystem.cs:1001-1051 |
| FireTagClip / FireTagSignal | AbilityExecSystem.cs:1216-1255、1259-1274 |
| EnterGate / ProcessGate | AbilityExecSystem.cs:1299-1382、1384-1493 |
| 终态映射 / rescan 上限 4 | AbilityExecSystem.cs:1655-1684、117-137 |
| 黑板键常量 / 订单失败原因 17 值 | src/Core/Gameplay/GAS/Orders/OrderBlackboardKeys.cs:26-38；OrderContinuationBuffer.cs:14-33 |

**相关文档**：[ab-02 PRD](../prd/ab-02-exec-timeline.md) · [ab-01 reference](ab-01-definition.md)
