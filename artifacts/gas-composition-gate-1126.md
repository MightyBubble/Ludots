# GAS Composition Gate — #1126 AwaitCallback / Graph Continuation

- **Date**: 2026-08-26
- **Branch**: `cursor/triggergraph-night-raid-land-e967` (PR #1239)

## 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**

结论: **PASS**

一句话理由: 一个 Yield 类原子 op + 既有引擎相位上的恢复队列；无 profile DSL、无平行事件总线、无第二 Graph VM。

## 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|---|---|---|
| AwaitCallback=455 | 0 | `HandleAwaitCallback`（注册句柄 + Status=Yielded） |
| GraphCallbackService | 0 | 句柄表 + Complete 队列 + 注册序 Drain |
| SystemGroup.Continuation | 0 | `GraphCallbackContinuationSystem` |
| TriggerGraph mount resume | 0 | `TriggerGraphMountTrigger` 实现 `IGraphCallbackResumeTarget` |

## 3. Reuse list

- Handlers: `HandleYield`、`ExecuteSlice`、mount park/`ResumeFromSuspension`
- Queues / Systems: `SystemGroup` 相位表、DeferredTriggerCollection 心跳续跑先例
- Registries: `ConfigKeyRegistry`（callbackType 符号）

## 4. New Layer 0 ops

| Op 名 | 单一职责 | 为何不能组合现有 op |
|---|---|---|
| AwaitCallback=455 | 注册具名回调句柄并挂起当前 run | 句柄生成与生命周期绑定必须发生在运行时 |

## 5. Transaction boundary

无；失效/双 complete/死 target 全部 fail closed。

## 6. Config SSOT

callbackType 走 ConfigKey 符号（与 DispatchMapEvent 事件名同一路径）。`Callbacks/callbacks.json` catalog 可后续增量，不阻塞本切片。

## 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 Dialogue Promise/协程塞进 runtime
- [x] 嵌套 InvokeScript 仍禁 ContainsYield（含 AwaitCallback）— 不做 silent yield-through

## 8. Next variant

Dialogue 宿主 Completer 已落地：`TryCompleteByCallbackType(DialogConfirm)` 在 `ChooseOption` / `AdvanceDialogue`；Drain 仍归 Continuation。剩余卫生：关 #1126 与 Epic #1083 子树关单。
