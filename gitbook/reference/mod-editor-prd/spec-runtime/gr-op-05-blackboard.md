# gr-op-05 runtime spec · 节点：黑板

> 引擎实现任务书。第一性需求见 [gr-op-05 PRD](../prd/gr-op-05-blackboard.md)；现状见 [reference](../reference/gr-op-05-blackboard.md)。

## 1. 概述

黑板读写合同：类型化键、Effect 专属写、与订单系统同池。

## 2. 设计

- Read/Write 保持"一次一实体一键"形态；键经 ConfigKeyRegistry 编译期解析。
- Write 归 Effect 事务：失败回滚时黑板写随事务丢弃。
- 图内键与订单内置键同池同缓冲：不做图私有命名空间。
- **治理项**：Read 掩码不含 Query（L+SC）——Query 图需要读黑板时无入口；若有实场景再议扩掩码，不新增平行节点。

## 3. 精确语义与不变量

- 键类型由注册表决定，节点类型必须与之相符。
- Read 缺省值语义与黑板缓冲约定一致（未建键读缺省，不建键）。

## 4. 迁移与治理

现状即基线；Query 掩码观察项入 TODO。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[gr-op-05 PRD](../prd/gr-op-05-blackboard.md) · [reference](../reference/gr-op-05-blackboard.md)
