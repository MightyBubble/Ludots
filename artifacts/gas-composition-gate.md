## GAS Composition Gate — Self Review

- **Task / Issue**: S12 · 寄存器归属与指令 descriptor（B14 / B18 / C12）
- **Date**: 2026-08-14
- **Agent / Author**: Cursor Grok 4.6

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 不新增 opcode、不拓宽 Script 方言、不改容量；只把已有 bump/Pin/硬编码 scratch 收进 `GraphRegisterFile`，把前门矩阵与 `GraphKindOperationPolicy` 三处例外收进一张 per-op descriptor 表并投影，并把 `GraphProgramSymbolPatcher.Patch` 修成幂等。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 寄存器分配 / scratch / Pin 冲突 | 0 | `GraphRegisterFile` + `GraphControlFlowCompiler` |
| kind × op 能力与端口 | 0 | `GraphOpDescriptorTable` |
| 装载期 kind 策略 | 0 | `GraphKindOperationPolicy`（读 descriptor 字段） |
| 作者前门 | 2 | `GraphProgramAuthoringFrontDoor` / ControlFlow 编译器（投影 descriptor） |
| 符号补丁幂等 | 0 | `GraphProgramSymbolPatcher` |

### 3. Reuse list

- Handlers: `GasGraphOpHandlerTable` 既有 metadata（Pure / GasTransactional / …），不改 handler 热路径
- Queues / Systems: 无
- Resolvers / Registries: `IGraphSymbolResolver`、`GraphIdRegistry`、覆盖表 `graph_node_op_coverage.registry.json`
- Existing presets / graphs: S9 `GraphEntityPreset`（E0/E1/E2）保留；`GraphVmLimits` 只提供容量 32

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| N/A | | |

禁止发明新 opcode。

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。本票是编译期归属与装载期策略投影，不改 effect 事务壳。

### 6. Config SSOT

行为配置落在: graph ControlFlow 文档 + `assets/Configs/GAS/graph_node_op_coverage.registry.json`（`authorableKinds` 由 descriptor 投影）

是否新增 JSON schema: NO — 不新增 profile / catalog schema；覆盖表字段集合不变，只改投影来源。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（Pin 冲突与 scratch 溢出失败关闭；Patch 第二次不得再解析）

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤

S13 才允许经 descriptor 拓宽 Script 方言；本票不改 Core enum。

### 复用 / 新增表

| 类型 | 项 |
|------|-----|
| 复用 | `GraphControlFlowCompiler`、`GraphKindOperationPolicy`、`GraphProgramAuthoringFrontDoor`、`GraphVmLimits`、`GraphEntityPreset`、`GasGraphOpHandlerTable` metadata、覆盖登记表 |
| 新增 Layer 0 | `GraphRegisterFile`（保留槽 + used-set + Alloc / AllocScratch / Pin） |
| 新增 Layer 0 | `GraphOpDescriptor` / `GraphOpDescriptorTable`（kinds × ports × operand roles） |
| 新增 Layer 1 | 无 |
| 新增 Layer 2 | 无（前门改为投影，不平行再造编译器） |
| 禁止 | 新 opcode、容量 32→N、Script 方言拓宽、平行 descriptor 生成器管线 |
