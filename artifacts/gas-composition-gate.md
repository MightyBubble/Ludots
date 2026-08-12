## GAS Composition Gate — Self Review

- **Task / Issue**: Retire legacy next-chain `GraphCompiler` per #861 / FuncLib-ActionLib contract
- **Date**: 2026-08-12
- **Agent / Author**: Cursor Cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 本次删除旧 next-chain 编译路径并迁移测试到已有 ControlFlow authoring，不新增 profile enum、preset 开关或平行加载器。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| Graph authoring compile SSOT | Layer 2 | `GraphProgramAuthoringFrontDoor` + `GraphControlFlowCompiler` |
| Runtime opcode execution | Layer 0 | `GasGraphOpHandlerTable` / `GraphExecutor` |

### 3. Reuse list

- Handlers: Existing `GasGraphOpHandlerTable` handlers.
- Queues / Systems: Existing graph execution entrypoints and GAS loaders.
- Resolvers / Registries: Existing `GraphProgramRegistry`, `GraphIdRegistry`, `GraphProgramSymbolPatcher`, `IGraphSymbolResolver`.
- Existing presets / graphs: Existing `GAS/graphs.json` ControlFlow documents and test ControlFlow documents.

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

N/A — no lifecycle transaction behavior is added or changed.

### 6. Config SSOT

行为配置落在: graph authoring assets (`GAS/graphs.json`) through `controlEdges` / `valueEdges`.

是否新增 JSON schema: NO.

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤
