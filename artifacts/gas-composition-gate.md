## GAS Composition Gate — Self Review

- **Task / Issue**: Dialogue Author Kit showcase（关口口令：分支对话 + MapVar 读写 + panelTheme 换肤）
- **Date**: 2026-08-27
- **Agent / Author**: cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A — 用既有 graph op（`ReadMapVarInt` / `WriteMapVarInt` / `HaltReturnInt`）组合 Query + TriggerGraph，再挂到 Dialogue 选项上

结论: PASS

一句话理由: 不新增 op / preset / profile enum；只新增 showcase 配置与薄运行时把现有 Dialogue + StoryPresentation + NarrativeFrontend 串起来。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 读地图变量判选项 | 2 | Query graph `Graph.AuthorKit.Condition.PassGranted` |
| 写地图变量记口令 | 2 | TriggerGraph `Graph.AuthorKit.Action.GrantPass` |
| 对话树 | 3（内容） | `Dialogue/dialogues.json` |
| 换肤 | 3（内容） | `game.json` `panelTheme` + `PanelThemes/` |

### 3. Reuse list

- Handlers: N/A（无新 BuiltinHandler）
- Queues / Systems: DialogueRuntime、StoryPresentationProjector、NarrativeFrontendService
- Resolvers / Registries: StoryDefinitionRegistry、MapVariableStore、PresentationDisplayResolver
- Existing presets / graphs: ReadMapVarInt / WriteMapVarInt / ConstInt / LoadCaster / HaltReturnInt

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

必须原子 rollback 的步骤: 无（单切片 Halt TriggerGraph 写 MapVar）

### 6. Config SSOT

行为配置落在: graph（`GAS/graphs.json`）+ Dialogue 节点引用 + 地图 `Variables`

是否新增 JSON schema: NO — 复用既有 Dialogue / Story / GAS / PanelThemes catalog 行

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤 / Core enum（只能选前两项之一）

选 graph 连线（例如再加一个 MapVar 门闩）
