# ab-07 runtime spec · 形态路由

> 引擎实现任务书。第一性需求见 [ab-07 PRD](../prd/ab-07-form-sets.md)；现状见 [reference](../reference/ab-07-form-sets.md)。

## 1. 概述

形态路由合同：每帧三步重算（清层→匹配→覆盖）、严格更大优先级、加载期全量校验与冻结。

## 2. 设计

- 路由保持：查三组件实体→形态槽 ClearAll→MatchesRoute（ContainsAll ∧ ¬Intersects，有效视角）→priority 严格更大才替换→SetOverride（槽号 ≥8 跳过）。
- 加载保持：routes/slotOverrides 非空、priority 必填、slotIndex 0..7、同路由重复槽号拒、abilityId 须已注册；加载后 Freeze。
- **治理项 AB5**：同分可同时匹配的路由无加载期校验（平分先出现者静默胜出）——加同分检测告警或文档化为合同。**AB6**：缺形态槽缓冲的 actor 静默无路由——启动/模板编辑器诊断缺组件单位。

## 3. 精确语义与不变量

- 形态层每帧从零重算，帧间无残余；优先级语义全局唯一（严格更大才替换）。

## 4. 迁移与治理
现状即基线；AB5/AB6 落地后回写。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[ab-07 PRD](../prd/ab-07-form-sets.md) · [reference](../reference/ab-07-form-sets.md)
