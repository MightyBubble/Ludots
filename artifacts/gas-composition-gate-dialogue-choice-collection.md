## GAS Composition Gate — Self Review

- **Task / Issue**: 对话选项进查询图 IntId 集合（artifacts/todo-dialogue-choice-graph-collection.md）
- **Date**: 2026-08-30
- **Agent / Author**: Cursor Cloud Agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**

结论: **PASS**

一句话理由: 交付是新 Graph 收集节点 + 既有 IntIdList → IntIdCollectionStore → Panel subject 组合；不新增 profile enum / preset 开关 / 平行物化管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 选项 string→int 身份 | 0（注册表） | `DialogueChoiceIdRegistry`（同 ProgressionIdRegistry） |
| 收集当前可选选项 | 0 op | `QueryCollectActiveDialogueChoices` |
| 写出类型化集合袋 | 2 | 现有 `GraphReturnWriter` + `DialogueChoiceCollection` destination |
| 面板消费 | 2 | 现有 `PanelHost` / `PanelListProjector` + `subject: DialogueChoice` |
| 会话/条件过滤 | 既有 | `DialogueRuntime.BuildAvailableChoices` |

### 3. Reuse list

- Handlers: `GasGraphOpHandlerTable` 注册新收集 handler；形态抄 `QueryCollectPresentTags` / `QueryCollectItemDefinitions`
- Queues / Systems: `PanelHost` 刷新、`GraphReturnWriter`、`IntIdCollectionStore`
- Resolvers / Registries: `IdentityTable`/`StringIntRegistry` 模式；`DialogueDefinitionRegistry` 装载期登记
- Existing presets / graphs: panel 集合袋 showcase 与 narrative 对话资产

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| `QueryCollectActiveDialogueChoices` | 把当前会话可选选项写成 IntIdList | 选项不在 Tag/Effect/Progression 容器里；唯一 SSOT 是 DialogueRuntime 已过滤名单 |

### 5. Transaction boundary

无多步物化事务；收集失败写空或抛错，不 rollback 实体结构。

### 6. Config SSOT

行为配置落在: 现有 `Dialogue/dialogues.json` + Query graph outputs + panel templates。  
是否新增 JSON schema: **NO**（只扩展已有封闭 enum：destination / subject / GraphNodeOp）。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（未接线 destination/subject 装载 fail-closed）

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线**（换 collectionKey / 元素 chip / CreatePanel），不改 Core enum。


## Follow-up complete — Narrative choices on PanelHost (2026-08-30)

- MapLoaded `CreatePanel(panel.narrative.choices)` once; Show/Hide from DialogueRuntime choice count.
- `StoryPresentationProjector` no longer emits ChoiceList companion.
- NarrativeShowcase playable acceptance green with PanelHost choice list.

## Follow-up complete — Bridge + AuthorKit (2026-08-30)

- Choice panel templates/graphs live in `NarrativeFrontendMod` (SSOT).
- `NarrativeStoryBridgeSystem` SyncVisibility for tagged maps.
- DialogueAuthorKit mounts `Graph.Narrative.Open.Choices`; acceptance asserts PanelHost, not ChoiceList.
- Removed dead `choiceAnchor` / `choiceLayoutId` profile fields.
