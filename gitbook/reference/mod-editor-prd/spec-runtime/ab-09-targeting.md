# ab-09 runtime spec · Targeting 与组合命令

> 引擎实现任务书。第一性需求见 [ab-09 PRD](../prd/ab-09-targeting.md)；现状见 [reference](../reference/ab-09-targeting.md)。

## 1. 概述

射程声明与组合计划合同：裁剪条件、移动锚点、续单链、取消传播、排队投影。

## 2. 设计

- 计划器 Submit 保持：构建"先移动后施放"计划；NotApplicable 直通、Rejected 拒；保活 actor + 续单安装；followUpCast 挂续单缓冲（键=移动单 OrderId，满→RejectedQueueFull），移动单提交失败回收续单，followUpCast 强制 Queued。
- 裁剪与投影保持：无 targeting / castRange≤0 / autoTargetPolicy≠None → 不适用；Queued 模式按移动完成后预计位置判原点；目标点=订单目标实体位置否则空间载荷；锚点=actor+方向×(距离−castRange) 停在射程边缘（≤castRange+0.01 视为在射程）。
- 传播保持：批量命令部分不可行整批拆分报错；移动单终态非 Completed→续单以 Cancelled/Failed 拒绝。

## 3. 精确语义与不变量

- 射程判定含 0.01cm 容差；锚点不低于射程边缘内侧；续单键唯一。

## 4. 迁移与治理
现状即基线；无新增设计项。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[ab-09 PRD](../prd/ab-09-targeting.md) · [reference](../reference/ab-09-targeting.md)
