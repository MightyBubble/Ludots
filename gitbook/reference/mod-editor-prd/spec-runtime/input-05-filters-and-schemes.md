# input-05 runtime spec · 过滤与输入方案

> 引擎实现任务书。第一性需求见 [input-05 PRD](../prd/input-05-filters-and-schemes.md)；现状见 [reference](../reference/input-05-filters-and-schemes.md)。

## 1. 概述
输入地基合同：动作词汇与上下文裁决、锚点展开过滤、方案套餐与切换白名单、轴移动节流、动作值直写属性。

## 2. 设计
- 方案保持：`ControlSchemeRuntime.Install/TrySwitch`；白名单空=全允许，非空即封闭集合。
- 轴移动保持：`AxisMoveOrderSystem` 按 throttleTicks/stepDistanceCm 节流提交订单。
- 属性绑定保持：全字段显式必填（无缺省路径）；三开关（UI 抢捕清零/滚轮抢占抑制/快照保持）语义保持。
- **治理项**：根默认输入的关键动作（Hotkey1-9、PrimaryClick 等）只绑在 `Physics2D_Playground`，默认玩法上下文 `Default_Gameplay` 缺绑定——补根绑定或提供"玩法上下文必绑清单"校验（O9）。

## 3. 精确语义与不变量
- 上下文优先级决定同抢裁决，高者胜且稳定可预期。
- 过滤结果 = 锚点展开集 − 排除 ∪ 包含（include 空则不筛）。
- 属性绑定的目标属性须已注册，写入走属性缓冲权威。

## 4. 迁移与治理
现状即基线；O9 根绑定补齐为引擎任务，落地后回写 reference。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[input-05 PRD](../prd/input-05-filters-and-schemes.md) · [reference](../reference/input-05-filters-and-schemes.md)
