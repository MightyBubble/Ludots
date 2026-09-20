# gr-op-13 runtime spec · 节点：拓扑谓词

> 引擎实现任务书。第一性需求见 [gr-op-13 PRD](../prd/gr-op-13-topology.md)；现状见 [reference](../reference/gr-op-13-topology.md)。

## 1. 概述

拓扑三谓词合同：一次查结构出值、无效实体缺省假、纯读。

## 2. 设计

- 三件各走一次控制域/知识投影查询；不做缓存穿透优化以外的复杂度。
- 无效实体语义固定：解析出无效句柄、判定返回假——不引入异常路径。
- **治理项**：Query 图按控制域/知情筛实体无管线件——若 AI 查询出现"只看看得见的敌人"实场景，考虑 Query 管线扩展而非把三件扩进 Query 掩码。

## 3. 精确语义与不变量

- 三件不改世界状态；同输入同输出。
- ControlDomainResolve 对自成域实体返回自身。

## 4. 迁移与治理

现状即基线；Query 管线观察项入 TODO。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[gr-op-13 PRD](../prd/gr-op-13-topology.md) · [reference](../reference/gr-op-13-topology.md)
