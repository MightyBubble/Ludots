## GAS Composition Gate — Self Review

- **Task / Issue**: MightyBubble/Ludots#1540 切A + #1554（EntityTemplate 递归 children 统一模型 → 跨 mod 地图实例合并 + 实例 relations + Relation* 事件）
- **Date**: 2026-09-20（终态重写；本文件为滚动"最新一次自检"）
- **Agent / Author**: 主代理开发，双轮 subagent 对抗性审查 + 三域逐行终审

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A——把既有 `EntityTemplate` 升格为唯一模板本位（递归 children / localId / attach），实体组即普通模板；实例差异走地图 `Maps/<id>.json` 同路径片段 by-instanceId 深合并 + `__delete` 墓碑；实例 relations 装载站物化复用 `RelationshipRuntime`；关系变更以四个 `Relation*` 预设事件暴露给 trigger 图。

结论: PASS

一句话理由: 零新模板类型、零第二物化路径、零新图节点；合并复用 `ConfigMerger.MergeObject`，事件复用 TriggerManager/schema fail-closed 管线，规则面不动（relation 自有规则引擎的退役归 #1570）。

### 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|-----------|-------|----------|
| 递归 children 装载/出生（双 lane 对齐） | 0 | MapLoader.SpawnTemplateChildNodes / RuntimeEntitySpawnSystem.EnqueueChildNodes |
| by-instanceId 合并 + 墓碑 + 报告 | 0 | MapManager.MergeEntityFragments / ResolvePendingTombstones / MapMergeReport |
| 实例 relations 物化 | 0 | InstanceRelationMaterializer（装载站，EnsureLink/SetMetric） |
| Relation* 四事件 | 0 | RelationshipProcessingSystem.PublishChangeEvents + EventSchemaRegistry 内置 schema |
| 变量合并对齐（墓碑/改型 fail-fast） | 0 | MapManager 变量分支 + MapVariableDeclarations 严格解析 |

### 3. Reuse list

模板/展开序/Overrides/可寻址路径/RelationshipRuntime/既有 Relationship* 图 op 20+/TriggerManager/EventSchemaRegistry/ConfigMerger.MergeObject——全部复用，零平行实现。

### 4. New Layer 0 ops

N/A（图节点零新增）

### 5. Transaction boundary

无新事务壳；装载站物化与参与者绑定同批、MapLoaded 前；变更走既有关系缓冲前缀分批。

### 6. Config SSOT

`Entities/templates.json`（递归 children）/ `Maps/*.json`（片段合并 + relations + variables + __delete）/ `Relationships/catalog.json`（词汇表）。无新 JSON schema 键族超出上述。

### 7. Red flag scan

- [x] 未新增第二种模板类型/prefab/槽位表（老槽位表已删净）
- [x] 未新建平行物化/合并/规则管线（relation 自有规则引擎退役另立 #1570）
- [x] 无静默 fallback（缺 instanceId/未 trim/未知 type·metric·to/同片段重复/活变量改型/边墓碑泄漏全 fail-fast；非首片段匿名实体可观测）
- [x] InstanceExposure/WatchedInstances 过期假实现已拆除，作者面单一形状

### 8. Next variant test

教科书 showcase 实机化（设计已落盘）：按键改关系 → Relation* 事件 → HUD；authoring 词汇随 #1570 切4（attribute 化）迁移。
