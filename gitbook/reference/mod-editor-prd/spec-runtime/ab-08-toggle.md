# ab-08 runtime spec · Toggle 技能

> 引擎实现任务书。第一性需求见 [ab-08 PRD](../prd/ab-08-toggle.md)；现状见 [reference](../reference/ab-08-toggle.md)。

## 1. 概述

Toggle 合同：开启（终态触发、容量先行、幂等）、关闭（先于激活门、摘 tag、可选关断时间轴）、效果回收路径。

## 2. 设计

- 开启保持：激活时间轴 Finished 触发，时点在订单收尾与 CastFinished 之前（容量预检失败须发生在玩家看到成功之前）；幂等；预检（队列缺失/不足抛容量错误）→AddTag→发布 ≤4 个 activeEffects 的无限时长 EffectRequest（Target=自身）。
- 关闭保持：先确保表现事件容量（有关断轴预留 2 CastStarted，否则 1 CastFinished+终态）→RemoveTag→有 DeactivateExecSpec 建 IsToggleDeactivating 新实例（推进换轴），无则瞬时完成；关闭分支在激活门之前（再激活冷却也不挡关）。
- 回收保持：activeEffects 靠效果被打 toggle tag 身份后由生命周期体系过期清理，关断逻辑不逐个撤销。

## 3. 精确语义与不变量

- 开启只发生在 Finished；Interrupted/Failed 不开启；activeEffects ≤4、关断轴不可嵌 toggleSpec。

## 4. 迁移与治理
现状即基线；无新增设计项。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[ab-08 PRD](../prd/ab-08-toggle.md) · [reference](../reference/ab-08-toggle.md)
