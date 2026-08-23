# GAS Composition Gate - TriggerGraph Entity Subworld

## GAS Composition Gate - Self Review

- **Task / Issue**: #1031 addendum: entity attachment tree as an explicit TriggerGraph aggregate scope
- **Date**: 2026-08-24
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**

结论: **PASS**

一句话理由: 聚落行为仍由现有 TriggerGraph entry 和现有 Materialize/Attach 原子 op 组合；本片只补实体事件归属边界，不新增 profile DSL、preset enum 或第二条物化管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 模板创建根实体与 children | 0/1 | 现有 `EntityBuilder`、`MaterializeTemplate`、`RuntimeEntitySpawnSystem` |
| 建立父子空间关系 | 0/1 | 现有 `AttachmentOps.Attach` / `RelationOps.SetParent` |
| 根实体多个反应图 | 2 | 现有 `EntityTemplate.TriggerGraphs` + `TriggerGraphMounting` |
| 子树事件归属 | 0 | 新增无状态 marker component + 现有 `ChildOf` 向上关系判定 |

### 3. Reuse list

- Handlers: 现有 `MaterializeTemplate`、`AttachmentOps.Attach`、TriggerGraph graph nodes
- Queues / Systems: 现有 `RuntimeEntitySpawnSystem`、`EntityLifecycleRuntimeServices`、`TriggerManager`、`GasEventTriggerBridgeSystem`
- Resolvers / Registries: 现有 `EntityTemplateKeyRegistry`、`GraphProgramRegistry`、`ComponentRegistry`
- Existing presets / graphs: 现有 `EntityTemplate.TriggerGraphs` 列表和 TriggerGraph entries

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| N/A | N/A | 事件归属是挂载宿主的判定，不是新的实体结构操作 |

### 5. Transaction boundary

必须原子 rollback 的步骤: 本片不改变事务边界；已有 Attach/Materialize 失败回滚继续负责结构一致性。

### 6. Config SSOT

行为配置落在: `EntityTemplate.TriggerGraphs` + 模板组件 `EntityTriggerGraphAggregateRoot`；图行为仍在 GAS graph assets。

是否新增 JSON schema: **NO** - 复用现有严格组件 authoring 和 TriggerGraph schema。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加“说不清的”默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线**

若选了 Core enum → FAIL
