# gr-op-09 runtime spec · 节点：聚合与迭代

> 引擎实现任务书。第一性需求见 [gr-op-09 PRD](../prd/gr-op-09-aggregate.md)；现状见 [reference](../reference/gr-op-09-aggregate.md)。

## 1. 概述

列表收口三件合同：单遍折叠、缺省值不报错、有效位约定。

## 2. 设计

- 三件保持单遍扫描零分配；AggMinByDistance 距离基准固定为 TargetPos，不参数化。
- TargetListGet 越界语义固定：无效句柄 + 有效位 0；不引入异常路径。
- **治理项**：TargetListGet 掩码不含 Query——Query 图取首元素要绕道 gr-op-07；若 Query 图出现按下标取元素的真实需求，扩掩码优于加平行节点。

## 3. 精确语义与不变量

- AggCount 空表恒 0；AggMinByDistance 空表恒无效句柄。
- 有效位只在 TargetListGet 求值时写一次，后续读取不重算。

## 4. 迁移与治理

现状即基线；Query 掩码观察项入 TODO。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[gr-op-09 PRD](../prd/gr-op-09-aggregate.md) · [reference](../reference/gr-op-09-aggregate.md)
