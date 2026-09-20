# gr-op-07 runtime spec · 节点：实体集查询

> 引擎实现任务书。第一性需求见 [gr-op-07 PRD](../prd/gr-op-07-entityset.md)；现状见 [reference](../reference/gr-op-07-entityset.md)。

## 1. 概述

实体集操作合同：Query 专属、建集-过滤-排序-聚合四段、空集语义成文。

## 2. 设计

- 建集两件保持一次物化：QueryAllMapEntities 全图快照、QueryFromCollection 集合键解析后复制入 TargetList。
- 过滤/排序只做原位收窄与重排，不重取集。
- 聚合空集语义集中定义一处（各聚合的空集产出），错误信息不掺业务默认值。
- **治理项**：QueryFilterTeam 的 TeamIdSource 旗标语义（立即值 vs 按 source 取队伍）目前只有描述符旗标无文档正本——在 rel-01/catalog 层补一段队伍语义归属。

## 3. 精确语义与不变量

- 整族掩码 = QueryOnly；非 Query 图编译拒绝。
- Max/MinEntityByAttribute 并列取序前元素，与稳定排序语义一致。
- 属性/tag 符号编译期解析失败即整图失败，不降级为空集。

## 4. 迁移与治理

现状即基线；队伍语义文档归属入 TODO（随 rel-01 立项）。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[gr-op-07 PRD](../prd/gr-op-07-entityset.md) · [reference](../reference/gr-op-07-entityset.md)
