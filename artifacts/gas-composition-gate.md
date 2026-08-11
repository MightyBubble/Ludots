## GAS Composition Gate — Self Review

- **Task / Issue**: #848 Graph VM — Query 与 Script 共用 ControlFlow 真引脚 IR；硬拒 Next 链（承载于 PR #859）
- **Date**: 2026-08-11
- **Agent / Author**: Cloud Agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A — 既有 graph 节点 / 编译器路径对齐（不新增 op enum）

结论: PASS

一句话理由: 把仓库在用的 Query ops 接到既有 `GraphControlFlowCompiler` 真引脚文档，并与 Script 对称硬拒 Next 链；不新增 profile/preset 开关。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| L0 指令执行 | 0 | 既有 `GraphInstruction` + `GasGraphOpHandlerTable` |
| Query 作者文档 | 1 | `GraphControlFlowDocument` + `GraphControlFlowCompiler` |
| 资产加载 | 2 | `GraphProgramConfigLoader`（CF 检测，拒混写；Query Next 硬拒） |
| Showcase/测试图 | 3 | Mod `GAS/graphs.json` + GasTests fixtures |

### 3. Reuse list

- Handlers: 既有 Query/Agg/Relationship handlers（无新 opcode）
- Queues / Systems: 无
- Resolvers / Registries: `GraphProgramRegistry`、`GraphOutputSchemaRegistry`、`IGraphSymbolResolver`
- Existing presets / graphs: fourx / entity_query_tactics / ui_player_aggregate / 相关测试 JSON

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

必须原子 rollback 的步骤: 无（编译期合同变更；运行时仍为 Query 只读聚合）

### 6. Config SSOT

行为配置落在: graph（`GAS/graphs.json` ControlFlow 文档）

是否新增 JSON schema: NO — 沿用 `controlEdges`/`valueEdges`/`outputs`；节点字段与既有 `GraphNodeConfig` 对齐

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（Query Next 链改为硬拒，与 Script 对称）

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线

---

## GAS Composition Gate — Track B Script Wait/While sugar

- **Task / Issue**: Script authoring sugar — `Wait`→`Yield`; `While`/`Until`→ JumpIfFalse + back-edge Jump（无新 opcode）；Effect/Score/Query/Validation/Derived 仍 Yield-forbidden
- **Date**: 2026-08-11
- **Agent / Author**: Cloud Agent (Track B)

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A — 既有 L0 op（Yield/Jump/JumpIfFalse）的作者层连线糖与编译展开

结论: PASS

一句话理由: Wait 是 Yield 的作者别名；While/Until 是 compile-time CF 糖，展开为既有 JumpIfFalse + Jump，不新增 While/Until/Wait opcode，也不把等待塞进 Effect skill。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| Yield / Jump / JumpIfFalse | 0 | 既有 `GraphNodeOp` + `GasGraphOpHandlerTable` / `ExecuteSlice` |
| Wait / While / Until 作者节点 | 1（编译糖） | `GraphControlFlowCompiler`（Script only） |
| Kind 禁 Yield | 1 政策 | 既有 `GraphKindOperationPolicy`（仅 Script 允许 Yield） |
| 验收测试 | 3 | GasTests Script wait/loop fixtures |

### 3. Reuse list

- Handlers: `Yield`, `Jump`, `JumpIfFalse`, `Call`/`Return`（#859 L0）
- Queues / Systems: 无
- Resolvers / Registries: `GraphProgramRegistry`、`GraphExecutionCursor`、`GraphVmLimits.MaxInstructionsPerExecution`
- Existing presets / graphs: `GraphScriptTestGraphs` / Script CF 文档模型

### 4. New Layer 0 ops (if any)

N/A — 禁止新增 While/Until/Wait opcode；Wait 编译为 Yield；While/Until 编译为 JumpIfFalse + Jump。

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。跨帧暂停仅 Script `ExecuteSlice`；Effect 事务路径不得 Yield（政策硬拒）。

### 6. Config SSOT

行为配置落在: Script `GraphControlFlowDocument`（`controlEdges`/`valueEdges`）

是否新增 JSON schema: NO — 仅新增作者 op 名字符串与 `body` 控制口；无 profile DSL。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（死循环撞 `MaxInstructionsPerExecution` 硬失败；Effect 带 Wait/Yield fail-closed）

### 8. Next variant test

「下一个 Mod 变体」将修改: Script graph 连线（Wait/While/Until 边），不改 Core enum / Effect skill。

