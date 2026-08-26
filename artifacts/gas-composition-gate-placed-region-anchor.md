## GAS Composition Gate — Self Review

- **Task / Issue**: #1108 余量 — `LoadPlacedRegion` / `LoadPlacedAnchor` + Bridge/面板；FSM-1a 验收关；叙事 AwaitCallback/Signal 展厅接线
- **Date**: 2026-08-26
- **Agent / Author**: Cursor Cloud Agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**

结论: **PASS**

一句话理由: 区域/锚点是两个只读 graph op（417/418），复用放置实体查表与 Regions 解析；不新增 profile enum、不把 region 塞进 EntityIndex、不另造 Promise/协程。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| LoadPlacedRegion / LoadPlacedAnchor | 0 | GraphNodeOp + GasGraphOpHandlerTable |
| 区域名册解析 | 0 | MapRegionDefinition.ParseList + BindRegionCatalogResolver |
| 锚点 kind 判定 | 0 | InstanceId 含 "anchor"（作者面/挂载校验） |
| Bridge `/instances` kind | 2 | Editor Bridge 投影 |
| FSM-1a 验收 | 2 | GraphFsmHostTests + HfsmSentryArena 诚实断言 |
| 叙事 Await/Signal 接线 | 2 | NarrativeShowcase graphs + Sequencer Signal + 验收测试 |

### 3. Reuse list

- Handlers: `HandleLoadPlacedEntity` 合同模板；`ConfigKeyRegistry` Imm 符号 patch
- Queues / Systems: 现有 `GraphCallbackService` + Continuation；`StoryGraphInvoker.ExecuteAction`
- Resolvers / Registries: `MapLoadEntityIndex`（实体/锚点）；`MapConfig.Regions`（区域，禁止进 EntityIndex）
- Existing presets / graphs: NarrativeShowcase TriggerGraph / Sequencer / Dialogue Completer

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| LoadPlacedRegion (417) | I[Dst]:=地图 Regions 名册是否含 Imm | LoadPlacedEntity 读的是实体索引，区域不是实体 |
| LoadPlacedAnchor (418) | E[Dst]:=锚点 InstanceId 实体 | 与 LoadPlacedEntity 运行时同源，作者面/挂载要求 InstanceId 含 anchor |

### 5. Transaction boundary

无 gameplay 事务；挂载期名册校验 fail-closed；运行时 miss 写 0 / Entity.Null（可读 miss，非 throw）。

### 6. Config SSOT

行为配置落在: graph JSON + map `Regions` / `Entities[].InstanceId`。是否新增 JSON schema: **NO**。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（Null/0 是 #1108 明确的可读 miss）

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线 / effect 步骤**。
