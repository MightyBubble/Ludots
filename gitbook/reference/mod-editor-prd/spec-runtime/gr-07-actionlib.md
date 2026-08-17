# gr-09 runtime spec · 动作库 ActionLib

> 引擎实现任务书。第一性需求见 [gr-09 PRD](../prd/gr-07-actionlib.md)；现状见 [reference](../reference/gr-07-actionlib.md)。

## 1. 概述

动作目录合同：字段封闭、宿主政策装载期裁决、双库命名空间。

## 2. 设计

- 字段四件封闭（name/graph/kind/host）；kind 门仅 Script；host 门四值枚举精确匹配。
- 撞名检查保持对 FuncLib 目录双向生效；宿主政策保持仅 BehaviorTree 与 Script 允许挂起。
- 装载期对 Hfsm/Level 动作做可达挂起校验（复用 FuncLib 纯度校验器基建）；装载位置保持 FuncLib 之后。

## 3. 精确语义与不变量

- 动作名唯一且与函数名不重叠；不可挂宿主的动作从入口不可达挂起；每个动作的图 id、kind、宿主三元组与注册表一致。

## 4. 迁移与治理

现状即基线；无新增治理项。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[gr-09 PRD](../prd/gr-07-actionlib.md) · [reference](../reference/gr-07-actionlib.md) · [gr-06 spec](gr-05-execution.md)
