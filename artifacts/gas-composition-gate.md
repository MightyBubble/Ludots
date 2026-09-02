## GAS Composition Gate — Self Review

- **Task / Issue**: #1398 Case E — 图内自写 hover + 去掉 Query 特权 + commit/tap 复用命中 + 热路径去分配
- **Date**: 2026-09-02
- **Agent / Author**: cursor cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A（op 组合：DispatchCollectionEvent + InvokeGraph 调命中函数 + continuous Execute）

结论: PASS

一句话理由: 预览由命中图自写；continuous 不强制 Query；commit/tap InvokeGraph 调同一张命中图；集合事件用 scratch+count 避免每帧 new Entity[]。

### 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|-----------|-------|----------|
| 每 tick 调命中图 | 2 | ContinuousQuerySystem → GraphReturnWriter.Execute（按注册 kind） |
| 写 hover | 1 | graph.case_e.box_hit · DispatchCollectionEvent |
| 松手/点选复用命中 | 2 | InvokeGraph → Query box_hit → 拷回 TargetList → 写 selected |
| 热路径载荷 | 1 | GasGraphRuntimeApi scratch + CollectionEntityCount |

### 3. Reuse list

- Handlers: DispatchCollectionEvent、InvokeGraph、ScreenRegionToEntities、EventKeyedCollectionWriter
- Systems: InteractionContextContinuousQuerySystem
- Graphs: graph.case_e.box_hit 唯一命中体

### 4. New Layer 0 ops

N/A（扩展既有 InvokeGraph 可调 Query；未新造 InvokeQuery）

### 5. Transaction boundary

无额外事务；失败关闭

### 6. Config SSOT

box_hit / box_commit / tap_commit / continuousQuery.graph / custom_events / collection_event_writers / case-e-config-structure.html

是否新增 JSON schema: NO（payload 增 CollectionEntityCount 进既有 transport family）

### 7. Red flag scan

- [x] 未新增 profile inherit enum
- [x] 未新建平行物化管线
- [x] 未 fallback
- [x] 未发明 InvokeQuery 特权名

### 8. Next variant test

改 box_hit 连线；commit/tap 应自动跟上
