## GAS Composition Gate — Self Review

- **Task / Issue**: Dialogue Author Kit — 拆掉 narrative_hosts 假 bootstrap，进图开聊改挂 MapLoaded TriggerGraph
- **Date**: 2026-08-27
- **Agent / Author**: cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**（新增 graph 节点 `StartDialogue`）

结论: **PASS**

一句话理由: 「进图开聊」是单一副作用，无法由现有 MapVar/FormalText/Panel op 组合出 DialogueRuntime.StartDialogue；禁止用一次性 Frontend hosts schema 冒充数据驱动。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| StartDialogue | 0 | GraphNodeOp + GasGraphRuntimeApi |
| MapLoaded 进图开聊 | 2 | TriggerGraph 组合（entries.event=MapLoaded） |
| 选项写/读口令 | 2 | 既有 WriteMapVarInt / ReadMapVarInt |
| 对话上屏 | 3 | NarrativeFrontend 通用投影（无 per-mod hosts） |

### 3. Reuse list

- Handlers: GasGraphOpHandlerTable、ConfigKeyRegistry 符号补丁
- Queues / Systems: TriggerGraphMounting、DialogueRuntime、StoryPresentationProjector
- Resolvers / Registries: DialogueDefinitionRegistry、GraphProgramRegistry
- Existing presets / graphs: FormalText MapLoaded 模式、AuthorKit GrantPass/PassGranted

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| StartDialogue | 按 dialogueId 启动 DialogueRuntime 会话 | 无既有 op 调用 DialogueRuntime；SinkPresentationText 只推字符串，不建对话会话 |

### 5. Transaction boundary

必须原子 rollback 的步骤: 无（StartDialogue 失败则 fail-closed 抛错；不静默）

### 6. Config SSOT

行为配置落在: TriggerGraph（`GAS/graphs.json`）+ 地图 `TriggerGraphs` 挂载

是否新增 JSON schema: **NO**（删除 `Frontend/narrative_hosts.json` 假 schema；仅复用既有 graph 节点字段 `dialogueId`）

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback
- [x] 删除 narrative_hosts / HostCatalog / bootstrap 硬编码挂靠

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线**（换 dialogueId / 换 MapLoaded 入口条件）

若选了 Core enum → FAIL — 本任务仅新增一个 Layer 0 op，后续变体不扩 enum。
