# rel-01 runtime spec · 关系目录

> 引擎实现任务书。第一性需求见 [rel-01 PRD](../prd/rel-01-catalog.md)；现状见 [reference](../reference/rel-01-catalog.md)。

## 1. 概述

九块词表合同：字段缺省、按 id 覆盖合并、姿态整替换。

## 2. 设计

- 九块 schema 保持封闭与缺省（度量 Min=-100/Max=100/Default=0；档位 Comparison 缺省 GreaterOrEqual；协同 MinimumCount=1；知识授予 ConfidencePermille=1000）。
- 合并语义保持：八块按 id 首现定序、后到覆盖整条目；stance 整对象替换；空 id 条目跳过。
- 反序列化收敛为仓库严格 JSON 约定（未知字段拒绝、Ordinal 大小写语义统一），错误带块与条目定位。

## 3. 精确语义与不变量

- 合并结果与片段顺序确定（按目录装载序）。
- 目录装载先于关系系统装配与图符号解析。

## 4. 迁移与治理

现状即基线；严格化随 cfg-04 id 体系统一任务推进。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[rel-01 PRD](../prd/rel-01-catalog.md) · [reference](../reference/rel-01-catalog.md) · [cfg-04 spec](cfg-04-config-tables.md)
