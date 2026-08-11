## GAS Composition Gate — Self Review

- **Task / Issue**: #858 UIP-2 PanelProjectionReader + GAS L1 SelectTagInMask / LookupTagDisplayToken
- **Date**: 2026-08-11
- **Agent / Author**: cursor cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**（新 graph 节点 + 既有 Attribute/GraphOutput 读口抽取）

结论: **PASS**

一句话理由: 面板绑定复用 AttributeBuffer / GraphOutputValueStore；Tag→展示文案补 Layer-0 原子 op + dense 查表，不新增 Presentation GraphKind / profile enum。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| PanelProjectionReader | 2（投影读口） | `src/Core/UI/PanelProjection/` |
| SelectTagInMask | 0 | `GraphNodeOp` + handler |
| LookupTagDisplayToken | 0 | `GraphNodeOp` + TagDisplayTableRegistry |
| TagDisplayTable 资产 | 2 配置 | `Presentation/tag_display_tables.json` |

### 3. Reuse list

- Handlers: `GasGraphOpHandlerTable`, `GasGraphRuntimeApi.HasTag` Effective 语义
- Queues / Systems: 无新队列；图仍走 `GraphReturnWriter`
- Resolvers / Registries: `TagRegistry`, `AttributeRegistry`, `PresentationTextCatalog`, `GraphOutputValueStore`
- Existing presets / graphs: MVP `ui_player_aggregate_graph_mvp` Summary keys

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| SelectTagInMask | 有效 tag ∩ mask → 单一 tagId | HasTag 无法表达互斥族选择/歧义 fail-closed |
| LookupTagDisplayToken | tagId → presentationTokenId | 无查表 op；禁止 Attribute 假冒文案 |

### 5. Transaction boundary

必须原子 rollback 的步骤: **无**（只读 Pure ops + 投影读口）

### 6. Config SSOT

行为配置落在: graph 连线 + `Presentation/tag_display_tables.json` + 既有 text_tokens/locales

是否新增 JSON schema: **YES** — `tag_display_tables.json`（tag→token 映射；无法用现有 Attribute/BB 表达）

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线** / table 行 / locale（不改 Core enum）
