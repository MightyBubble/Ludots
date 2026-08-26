# GAS Composition Gate - PR1105 Graph editor control flow / live trace

- **Task / Issue**: #1105 Harden Graph editor control flow and live debug
- **Date**: 2026-08-26
- **Agent / Author**: Cursor Cloud Agent

## 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**

结论: **PASS（控制流/黑板/trace 合同；文本能力未开放）**

一句话理由: 节点联想、作者糖与控制端口全部从 Bridge 投影运行时 descriptor/compiler；不新增 Graph VM、事件总线或 editor 假节点。

## 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|-----------|-------|----------|
| 控制输出端口投影 | 2 | Editor Bridge descriptor + React |
| 作者糖 BranchBool/SwitchInt/Wait/While/Until/Break | 2 | 已有 GraphAuthoringSugar + GraphControlFlowCompiler |
| Live trace GraphId | 2 | GraphDebugTrace + GraphExecutor/InvokeScript |
| Source map fail-closed | 2 | GraphDebugTool |

## 3. Reuse list

- GraphOpDescriptorTable / GraphAuthoringSugar / GraphControlFlowPorts
- GraphDebugTrace 固定容量 ring
- 已有删节点悬挂边清理、layout schema 校验

## 4. New Layer 0 ops

N/A

## 5. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建平行 Graph VM / 事件总线
- [x] 未添加默认 fallback；缺 descriptor / source map 显式失败
- [x] 未声称 NodeExit 完整生命周期

## 6. Next variant

字符串模板 / Concat 仍需独立 text value 合同后再开放。
