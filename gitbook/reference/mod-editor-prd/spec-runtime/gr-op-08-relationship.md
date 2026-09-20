# gr-op-08 runtime spec · 节点：关系系统

> 引擎实现任务书。第一性需求见 [gr-op-08 PRD](../prd/gr-op-08-relationship.md)；现状见 [reference](../reference/gr-op-08-relationship.md)。

## 1. 概述

关系族合同：三段掩码、目录符号、效果组合的 fail-closed 域。

## 2. 设计

- 写侧五件保持 Effect 事务语义；reason 记账位（dst）保留，供外部追溯。
- 效果组合编译对写侧按 Relationship 域 fail-closed：Unsupported 元数据集中声明，不做逐处特判。
- 读侧与管线只读；管线 list+source 双输入语义固定（source 判关系、list 被筛）。
- 度量整数世界与属性浮点世界分离：不提供隐式互转。
- **治理项**：BetweenPair 与 Mutual 的差异（点对间 vs 双向链）只在代码语义里，缺用户面文档锚——在 rel-01 落地时补对比表。

## 3. 精确语义与不变量

- EnsureLink 幂等：对已存在链重执行不产生第二条。
- 聚合族空集语义与 gr-op-07 同一处定义。
- 目录符号解析失败即整图失败，无降级。

## 4. 迁移与治理

现状即基线；Mutual/BetweenPair 对比表随 rel-01 立项。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[gr-op-08 PRD](../prd/gr-op-08-relationship.md) · [reference](../reference/gr-op-08-relationship.md)
