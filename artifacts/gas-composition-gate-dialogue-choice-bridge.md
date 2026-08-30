## GAS Composition Gate — Self Review

- **Task / Issue**: 对话选项 PanelHost 收口补齐 — NarrativeFrontend 桥接 + DialogueAuthorKit 对齐（不再留 ChoiceList）
- **Date**: 2026-08-30
- **Agent / Author**: cursor cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**（把已有 `QueryCollectActiveDialogueChoices` / `CreatePanel` / `ShowPanel` 图与模板抽到 NarrativeFrontend capability，桥接只做 Show/Hide）

结论: **PASS**

一句话理由: 不新增 op / profile enum；只搬家与接线，让带 `narrative.frontend.project` 标签的地图与旗舰 showcase 共用同一选项面板合同。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 收集可选回复 | 2 | 已有 `QueryCollectActiveDialogueChoices` → `DialogueChoiceCollection` |
| 创建选项面板 | 2 | 已有 `CreatePanel(panel.narrative.choices)` MapLoaded 图 |
| 有选项显示 / 无选项隐藏 | 3 | `PanelActivationApi` + bridge/showcase Sync |
| 主对话框 | 3 | 仍 NarrativeFrontend 字符串袋（本切片不动；合同禁止塞进 PanelHost 数值模板） |

### 3. Reuse list

- Handlers: CreatePanel / ShowPanel / HidePanel / QueryCollectActiveDialogueChoices
- Queues / Systems: PanelHost、PanelActivationApi、NarrativeStoryBridgeSystem
- Resolvers / Registries: DialogueChoiceIdRegistry、PanelTemplateRegistry
- Existing presets / graphs: Graph.Narrative.Choices / Choice.Chip / Open.Choices（从 showcase 上收到 capability）

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

无 lifecycle 事务；Show/Hide 为幂等可见性写入。

### 6. Config SSOT

行为配置落在: `NarrativeFrontendMod/assets/Panels/panel_templates.json` + `GAS/graphs.json`（capability 单一来源）

是否新增 JSON schema: **NO** — 删除 profile 上已死的 `choiceAnchor/choiceWidth/choiceLayoutId` 字段

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线**（换 collectionKey / 锚点 / CreatePanel scope），不改 Core enum。
