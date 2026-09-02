## GAS Composition Gate — Self Review

- **Task / Issue**: #1398 Case E 宪法 05 — boxing context 下每帧命中 query → 预览集合成员变化 → presenter 高亮
- **Date**: 2026-09-02
- **Agent / Author**: cursor cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**（Query graph 连线 + 与现有 `triggers[]` 同构的 profile 挂载字段 `continuousQuery.graph`）

结论: **PASS**

一句话理由: 命中算法落在 Query graph（`QueryFromCollection` + `ScreenRegionToEntities`）；profile 只声明「本 context 活着时每帧跑哪张 Query」，与已有 `triggers[]` 挂载同型，不是 inherit/placement enum 或行为开关 DSL。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 屏幕矩形命中 | 2 | Query graph op 组合 |
| 候选集入参 | 2 | `QueryFromCollection(case_e.selectable)` |
| 预览集合物化 | 2 | GraphReturnWriter → EntityCollectionStore.Replace |
| 成员变化 → 高亮 | 2 | EntityCollectionPresentationEventSystem + presenter rules |
| context 持续调度 | 2/基建 | InteractionContextContinuousQuerySystem（挂 profile.continuousQuery） |

### 3. Reuse list

- Handlers: GraphReturnWriter、GasGraphRuntimeApi、ScreenRegionToEntities / QueryFromCollection
- Queues / Systems: EntityCollectionPresentationEventSystem、InputActionAttributeBindingSystem、AbilityAim hover Replace 模式
- Resolvers / Registries: InteractionContextProfileRegistry、GraphProgramRegistry、GraphOutputSchemaRegistry、EntityCollectionStore
- Existing presets / graphs: Case E box_commit 命中链；entity_query_tactics Query+outputs

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

无跨实体事务；预览 Replace 与 commit 写 selected 分属不同集合 key，失败即抛（NO fallback）。

### 6. Config SSOT

行为配置落在: Query graph `graph.case_e.box_hit` + profile `continuousQuery` + presenter rules（`case_e.box_hover`）

是否新增 JSON schema: **YES** — `continuousQuery.graph` 与 `triggers[]` 同为「context 挂载图引用」，无法用单张 graph 表达「每帧调度」（TriggerGraph 无 Held/EveryFrame）；调度是 context 生命周期基建，挂载点必须在 profile。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线**（换候选 key / 矩形来源属性 / 预览 collectionKey 于 Query outputs）

若选了 Core enum → FAIL — 未选。
