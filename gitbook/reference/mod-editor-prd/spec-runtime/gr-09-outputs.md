# gr-09 runtime spec · Query 图输出

> 引擎实现任务书。第一性需求见 [gr-09 PRD](../prd/gr-09-outputs.md)；现状见 [reference](../reference/gr-09-outputs.md)。

## 1. 概述

输出 schema 校验、回写物化、槽位存储与清理四件合同。

## 2. 设计

- schema 校验保持编译期完成：destination/type 封闭、EntityCollection↔TargetList 绑定、collectionKey 必填、Summary 禁 TargetList、source 存在且类型匹配、key 缺省 outputId。
- 回写器保持四前置（仅 Query、RequireAllowed、有 schema、owner/caster 非空）与帧绑目标上下文；EntityCollection 建描述符整表替换，Summary 按键写四类标量。
- 值存储保持 SOA 槽池 + 双哈希 + 世代/修订号 + 退休队列；容量来自 gasRuntimeCapacity；清理系统订阅实体销毁。

## 3. 精确语义与不变量

- outputs 只属于 Query 图；同一 owner 同一 collectionKey 物化为整表替换不叠加；退休槽不可读。

## 4. 迁移与治理

现状即基线；治理项 G6（Query 物化链路空转——零资产零调用）见 todo/graph.md。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[gr-09 PRD](../prd/gr-09-outputs.md) · [reference](../reference/gr-09-outputs.md) · [gr-08 spec](gr-08-mount-points.md)
