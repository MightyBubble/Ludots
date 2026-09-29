## GAS Composition Gate — Self Review

- **Task / Issue**: Case E 删除档案上的 activeCollectionKey，集合键改由图节点声明并记到实体交互实例
- **Date**: 2026-09-28
- **Agent / Author**: Cursor cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 不新增档案字段。已有提交图节点增加可选 collectionKey，安装期抄到已有的 InteractionContextInstance。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 集合键声明 | 2 | SubmitCommandIntent / SubmitCast / SubmitEngageBatch 的 collectionKey |
| 记到实体 | 0 已有组件 | InteractionContextInstance.ActiveCollectionKeyId |
| 拒绝平行字段 | 装载校验 | InteractionContextProfileConfigLoader |

### 3. Reuse list

- Handlers: GasGraphOpHandlerTable 的提交 op 不改执行语义
- Queues / Systems: CommandIntentBufferDrainSystem 仍读实体上的键
- Resolvers / Registries: InteractionContextProfileRegistry、EntityCollectionStore 键表、GraphProgramSymbolPatcher
- Existing presets / graphs: graph.case_e.command_commit、graph.case_e.cast_q、graph.case_e.selection_commit

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。键在上下文安装期解析，失败即启动失败。

### 6. Config SSOT

行为配置落在: graph（`collectionKey`）+ 实体交互实例

是否新增 JSON schema: NO — 档案侧收掉平行集合字段，不新增开关。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线

若选了 Core enum → FAIL
