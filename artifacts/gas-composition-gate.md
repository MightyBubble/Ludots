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
