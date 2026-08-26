## GAS Composition Gate — Self Review

- **Task / Issue**: Query-graph collection outputs §3.8 items 2–7 (templates / items+aggregate / ability slots+nested / reverse+source=input / task·activity / tag·progression) on top of Effect-instance slice (#1272)
- **Date**: 2026-08-26
- **Agent / Author**: Cursor Cloud Agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**

结论: **PASS**

一句话理由: 新增的是查询图 Collect* 节点、类型化集合 destination（IntId 袋 / 实体袋）、以及面板对已声明 subject·source·present 的消费接线；不新增 profile enum、preset 开关或平行物化管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| Collect* 读容器/注册表填 TargetList 或 IntIdList | 0 | `GraphNodeOp` + `GasGraphOpHandlerTable` + `IGraphRuntimeApi` |
| 类型化集合写出 | 1/2 | `GraphReturnWriter` → `EntityCollectionStore` / `IntIdCollectionStore` |
| 面板 subject / source=input / nested / aggregate | 2 | `PanelListProjector` + `PanelPresentationSystem` + 模板装载 |
| Showcase 验收 | 2 | showcase mods + acceptance tests |

### 3. Reuse list

- Handlers: 现有 `GasGraphOpHandlerTable` / Query 编译器 / `GraphReturnWriter` 模式（对齐 `QueryCollectActiveEffects`）
- Queues / Systems: 无新 lifecycle；只读收集
- Resolvers / Registries: `EffectTemplateIdRegistry`、`AbilitySlotResolver`、`TagRegistry`/`TagOps`、`ProgressionStateBuffer`、`InventoryRuntimeService`、Task/Activity 实例组件
- Existing presets / graphs: 现有 panel_effect_list 竖切；集合合同 `query-graph-collection-outputs.md`

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| QueryCollectEffectTemplates | 写出效果模板 id 袋 | 无现成枚举注册表→TargetList/IntIdList 的节点 |
| QueryCollectAbilitySlots | 写出单位有效技能槽下标 | 槽位不是实体，不能复用 CollectActiveEffects |
| QueryCollectInventoryItems | 写出背包物品实例实体袋 | 无 span 友好的正式收集节点 |
| QueryCollectItemDefinitions | 写出物品定义 id 袋 | 同上，定义 id |
| QueryCollectPresentTags | 写出实体当前标签 id 袋 | 标签不是实体 |
| QueryCollectActiveTasks | 写出范围内任务实例实体袋 | 需明确 scope 语义的收集节点 |
| QueryCollectProgressionNodes | 写出实体进度 id 袋 | ProgressionStateBuffer 枚举 |
| QueryCollectAbilityHolders | 候选实体中筛出持有指定技能者 | 反查样板；约束来自输入/标志 |

（若实现中合并/更名，以最终 `GraphOps.cs` 为准；职责不变。）

### 5. Transaction boundary

必须原子 rollback 的步骤: **无**（只读查询 + 集合替换写出；集合 Replace 自身为整袋替换）

### 6. Config SSOT

行为配置落在: 现有 `graphs.json` outputs + `panel_templates.json` collections/inputs/present；showcase 自有 assets。

是否新增 JSON schema: **NO** — destination / subject / present 为既有合同字段的运行时接线。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（未接线 subject/destination/source 继续 fail-closed）

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线 / effect 步骤**
