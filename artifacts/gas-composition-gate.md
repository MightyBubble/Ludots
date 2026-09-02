## GAS Composition Gate — Self Review

- **Task / Issue**: #1398 Case E — 命中图自己写 hover，去掉 GraphReturnWriter 代写偷鸡
- **Date**: 2026-09-02
- **Agent / Author**: cursor cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A（op 组合：既有 DispatchCollectionEvent + 既有 continuous tick 调度）

结论: PASS

一句话理由: 预览集写入改回图内 DispatchCollectionEvent；调度只 Execute，不再 GraphReturnWriter 物化 outputs。Query 策略改为：仅当 op 明确可创作于 Query 时，才放行带 WorldSideEffect 的 Pure 元数据（放行 DispatchCollectionEvent，继续拦 ShowPanel 等 #1410 禁令）。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 每 tick 调命中图 | 2 | InteractionContextContinuousQuerySystem → GraphReturnWriter.Execute |
| 写 hover 集 | 1 | graph.case_e.box_hit · DispatchCollectionEvent |
| 成员变化 → 黄环 | 2 | EventKeyedCollectionWriter + EntityCollectionPresentationEventSystem + presenter rules |
| 离开清空 hover | 2 | ContinuousQuerySystem 扫图内 DispatchCollectionEvent 的 collection key 后 Replace 空 |

### 3. Reuse list

- Handlers: DispatchCollectionEvent、ScreenRegionToEntities、QueryFromCollection、EventKeyedCollectionWriter
- Queues / Systems: InteractionContextContinuousQuerySystem、EntityCollectionPresentationEventSystem
- Resolvers / Registries: InteractionContextProfileRegistry（校验图必须含 DispatchCollectionEvent）
- Existing presets / graphs: graph.case_e.box_hit、collection_event_writers、custom_events

### 4. New Layer 0 ops (if any)

N/A（仅放宽 DispatchCollectionEvent / ConstInt 对 Query 的可创作白名单；Query 策略按 AuthorableKinds 放行该 op 的 WorldSideEffect）

### 5. Transaction boundary

必须原子 rollback 的步骤: 无（每 tick Replace 语义写 hover；失败关闭）

### 6. Config SSOT

行为配置落在: graph.case_e.box_hit + Events/custom_events + Input/collection_event_writers + interaction_context_profiles.continuousQuery.graph

是否新增 JSON schema: NO — 复用 DispatchCollectionEvent；去掉对 outputs[] 的 continuous 依赖

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线
