# rt-05 runtime spec · 事件总线与帧延迟

> 引擎实现任务书。第一性需求见 [rt-05 PRD](../prd/rt-05-events.md)；现状见 [reference](../reference/rt-05-events.md)。

## 1. 概述

分拍事件合同：双缓冲总线、一拍延迟、fail-fast 发布、写事务回滚；响应链遥测旁路。

## 2. 设计

- 双缓冲保持：定容两数组（容量=引擎常量，事实页）；Publish 写 next 满抛；Update 交换并结算丢弃计数；读只暴露 current 只读视图。
- 写检查点/回滚保持：事务失败恢复 next 计数与标志——幽灵事件不出现在任何一拍。
- 组序保持：AbilityActivation → EffectProcessing → EventDispatch（帧末交换）；同帧等待者读 current 的现状语义保持（tag-03 同源）。
- **治理项 R3**：ResponseChainTelemetryBuffer 溢出为静默丢弃+计数（TryAdd 失败计 DroppedSinceClear/DroppedTotal），与表现事件（满抛）、诊断缓冲（满抛）、主总线（满抛）不一致——同为"观测/遥测"通道行为却分两派。方向：明确遥测合同（观测面允许有界丢弃但必须显式计数可查），或统一 fail-fast；裁决前在架构文档写明两派分界，编辑器 UI 按现状如实呈现。

## 3. 精确语义与不变量

- 一拍延迟恒定：本帧发布最早下一拍对跨帧消费者可见。
- 交换是唯一的状态转移点；交换后 current 在本拍内只读。
- 回滚后 next 内容与检查点时一致；发布满抛错误带容量与 tag id。

## 4. 迁移与治理

现状即基线；R3 处置入 TODO（见 todo/runtime.md）。

## 变更记录

- v1（2026-08-17）：初版。

**相关文档**：[rt-05 PRD](../prd/rt-05-events.md) · [reference](../reference/rt-05-events.md)
