# ab-05 runtime spec · 激活门

> 引擎实现任务书。第一性需求见 [ab-05 PRD](../prd/ab-05-activation-gates.md)；现状见 [reference](../reference/ab-05-activation-gates.md)。

## 1. 概述

激活判序合同：直接激活与订单起播两入口、tag 门语义、前置图评估点、进度需求延迟评估。

## 2. 设计

- 直接激活入口判序保持：存活→状态缓冲→目标校验（TargetContext/显式目标/集合全存活；validationTarget=显式或集合首或施法者）→槽位→tag 门→前置图→进度需求；订单起播入口差异保持：toggle 先关、进度先于前置（带目标坐标）、进度可延迟到门响应后评估。
- 失败映射保持：PreconditionFailed→同名；其余→ActivationBlocked；挂起的进度需求遇非门条目直接失败。
- **治理项**：直接激活入口生产调用方仅 ReactionSystem 一处——入口收敛评审（收敛则删判序差异，保留则文档化为合同）。

## 3. 精确语义与不变量

- tag 门：无 tag 单位仅空 requiredAll 放行；否则 ¬Intersects(blockedAny) ∧ ContainsAll(requiredAll)（有效视角）；两入口判定件同源。

## 4. 迁移与治理
现状即基线；入口收敛评审立项后回写。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[ab-05 PRD](../prd/ab-05-activation-gates.md) · [reference](../reference/ab-05-activation-gates.md)
