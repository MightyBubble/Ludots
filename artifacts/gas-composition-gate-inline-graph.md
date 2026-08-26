# GAS Composition Gate — InlineGraph compile-time macro

- **Date**: 2026-08-26
- **Branch**: `cursor/inline-graph-macro-e967`

## 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**（编译期拼文档，零新 opcode）

结论: **PASS**

一句话理由: 可等待复用对齐虚幻 Macro（内联）；`InvokeGraph` 保持同步 Function；不新造协程/Promise/第二 VM。

## 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|---|---|---|
| InlineGraph 糖名 | 作者面 | `GraphAuthoringSugar.InlineGraph` |
| 装载前展开 | 0 旁路织入 | `TriggerGraphInlineWeaver` |
| Await/Yield | 0 | 既有 `AwaitCallback` / Continuation |

## 3. Reuse list

- HookWeaver 片段抽取/前缀/换边模式
- GraphProgramConfigLoader 装载序（先 Expand 再 Compile）
- ContainsYield 对 InvokeGraph 的同步禁令（故意保留）

## 4. New Layer 0 ops

N/A

## 5. Red flag scan

- [x] 未新增 profile enum
- [x] 未做 runtime yield-through
- [x] 未平行 Promise
- [x] 未知宏 / 环 / 深度 / 非 Halt 终端 fail closed
