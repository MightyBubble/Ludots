# rel-01 runtime spec · 关系目录

> 引擎实现任务书。第一性需求见 [rel-01 PRD](../prd/rel-01-catalog.md)；现状见 [reference](../reference/rel-01-catalog.md)。

## 1. 概述

四块词表合同：`catalog.json` 的 types / metrics / flags 和 `projection.json` 的 knowledgeGrants，字段缺省、按 id 覆盖合并。

## 2. 设计

- 四块 schema 封闭（度量 Min=-100 / Max=100 / Default=0；知识授予 ConfidencePermille=1000，合法范围 1..1000）。
- 合并语义：各块按 id 首现定序、后到覆盖整条目。
- 反序列化拒绝未知字段；块放错文件、条目为 null、缺 id 都装载失败，错误带片段、块与下标。

## 3. 精确语义与不变量

- 合并结果与片段顺序确定（按目录装载序）。
- 目录装载先于关系系统装配与图符号解析。
- 架构守卫 `RelationshipCatalogs_DeclareOnlyConsumedVocabulary` 拦截未知块和占位参与类型（`Participant` / `*.Participant`）。

## 4. 迁移与治理

- 已退役：reasons / bands / callbacks / synergies 四块，stance 块，以及各 mod 的 `*.Participant` 占位类型。
- 待清：档位、回调、协同的运行时结构（`RelationshipBandRegistry`、`RelationshipCallbackProcessor`、`RelationshipSynergyProcessor`）已无数据来源，需要单独删除。

## 变更记录

- v1（2026-08-15）：初版。
- v2（2026-09-29）：块收敛为四块，未知字段拒绝，缺 id 报错，knowledgeGrants 固定在 projection.json。

**相关文档**：[rel-01 PRD](../prd/rel-01-catalog.md) · [reference](../reference/rel-01-catalog.md) · [cfg-04 spec](cfg-04-config-tables.md)
