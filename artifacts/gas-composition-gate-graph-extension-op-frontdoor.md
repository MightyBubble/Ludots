## GAS Composition Gate — Self Review

- **Task / Issue**: 第 2 档图节点：把 `RegisterGraphOp` 接到 L1 ControlFlow 前门（#861 同一扇门）；不复活 next 链。
- **Date**: 2026-09-11
- **Agent / Author**: cursor-graph-mod-op-extension-1ec9

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 接上已有扩展算子登记表，让图 JSON 按名引用 Mod 原子节点；不新增 profile enum / 平行 VM / 新 Core opcode 枚举成员。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| Mod 登记原子算子 | 0（已有槽 ≥1024） | `RegisterGraphOp` / `GasGraphOpRegistry` |
| 作者图引用键 | 2 | `graphs.json` ControlFlow + FrontDoor |
| 编译解析与发射 | 0 编译器 | `GraphControlFlowCompiler` |
| 执行 | 0 | `GasGraphOpHandlerTable.InstallExtensions` |

### 3. Reuse list

- Handlers: `GasGraphOpHandlerTable.InstallExtensions`
- Queues / Systems: 现有 Graph VM / `GraphExecutor`
- Resolvers / Registries: `GasGraphOpRegistry`、`GraphProgramAuthoringFrontDoor`、`GraphKindOperationPolicy`
- Existing presets / graphs: 现行 `controlEdges` / `valueEdges`；禁止 `nodes[].next`

### 4. New Layer 0 ops (if any)

N/A。不新增 `GraphNodeOp` 枚举。Mod 占用 ≥1024 动态槽。

### 5. Transaction boundary

编译失败不登记；`ReplaceProgram` 织入失败 rollback（既有）。扩展算子默认 Pure 元数据，Score/Script 策略与内建纯算子一致。

### 6. Config SSOT

行为配置落在: graph（`GAS/graphs.json`）引用已登记键。

是否新增 JSON schema: NO

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（未知键 / Query 上的扩展算子 / 缺输入均失败关闭）

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤 / Core enum（只能选前两项之一）

下一个 Mod 变体改 graph 连线（引用已登记 `我的Mod.某某`），或再登记一个 handler；不改 Core enum。
