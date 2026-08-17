# fx-14 runtime spec · 目标过滤

> 引擎实现任务书。第一性需求见 [fx-12 PRD](../prd/fx-10-target-filter.md)；现状见 [reference](../reference/fx-10-target-filter.md)。

## 1. 概述

候选过滤链的顺序、敌我判定与容量语义合同。

## 2. 设计

- 过滤顺序保持：ExcludeSource→Ring 内径→LayerMask→Relationship→容量→根预算；顺序不变量不得重排。
- 敌我与容量保持：六值关系，双方须有 Team 否则滤除（缺阵营=不可判定不放行）；上限 0=无限、非零截前 N；层掩码可选、层名走层注册表。

## 3. 精确语义与不变量

- 过滤链只收窄候选集不重排序（截取按候选序）；敌我判定不产生副作用（不缓存关系结论）。

## 4. 迁移与治理

现状即基线；与 E2 治理联动：过滤参数唯一正路为本块，查询描述符侧四个同名死字段删除后不得复活（@@fx8@@ spec）。

**变更记录**：v1（2026-08-15）：初版。

**相关文档**：[fx-12 PRD](../prd/fx-10-target-filter.md) · [reference](../reference/fx-10-target-filter.md)
