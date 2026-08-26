# GAS Composition Gate — Dialogue AwaitCallback Completer

- **Date**: 2026-08-26
- **Branch**: `cursor/dialogue-await-callback-e967`

## 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**（复用既有 AwaitCallback + Complete 队列）

结论: **PASS**

一句话理由: Dialogue 只做 Completer，不新增 opcode、不实现第二 resume target、不平行等待机制。

## 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|---|---|---|
| 按 callbackType 查找最老 live handle | 0 | `GraphCallbackService.TryGetOldestLiveHandleByCallbackType` |
| Dialogue 确认完成句柄 | 2 宿主 | `DialogueRuntime.CompletePendingDialogConfirm` |
| Drain | 0 | 既有 `GraphCallbackContinuationSystem` |

## 3. Reuse list

- `GraphCallbackService.Complete` / `Drain`
- `GraphCallbackTypes.DialogConfirm`
- Dialogue 提交点 `ChooseOption` / `AdvanceDialogue`

## 4. New Layer 0 ops

无。

## 5. Red flag scan

- [x] 未新增 profile enum
- [x] Dialogue 未实现 `IGraphCallbackResumeTarget`
- [x] Story action 仍要求单切片 Halt（同步权威点不变）
