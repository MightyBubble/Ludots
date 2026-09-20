# ab-03 runtime spec · CallerParams 参数池

> 引擎实现任务书。第一性需求见 [ab-03 PRD](../prd/ab-03-caller-params.md)；现状见 [reference](../reference/ab-03-caller-params.md)。

## 1. 概述

参数池合同：内联四组存储、条目索引引用、空间参数注入、两条合并路径。

## 2. 设计

- 池保持 MAX_SETS=4 内联（_p0.._p3）；条目 callerParamsIdx 0xFF=无；触发效果条目时取所引组入 EffectRequest.CallerParams。
- 空间注入保持：有目标位置追加 TargetPosX/Y、有原点追加 TargetOriginX/Y（TryAdd 成对）；合并两路径保持：实体路径优先预合并组件，请求路径同键覆盖、满静默丢。
- **治理项 AB3**：空间参数追加失败整技能报 PreconditionFailed、不指明是池余位不足——拆独立容量不足原因码并携带池状态。

## 3. 精确语义与不变量

- 组数 >4、单组超上限：编译期拒绝；callerParamsIdx 越界现状按"无参数"处理（保持事实，编辑器侧先行拦截）。

## 4. 迁移与治理
现状即基线；AB3 落地后回写 reference。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[ab-03 PRD](../prd/ab-03-caller-params.md) · [reference](../reference/ab-03-caller-params.md)
