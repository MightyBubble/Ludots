# GAS Composition Gate — Graph Codegen 产品化

- **Task / Issue**: 图 Codegen 全覆盖产品化设计（#860 后端替换升格；审计 C23）
- **Date**: 2026-08-26
- **Agent / Author**: Cursor Cloud Agent on `cursor/graph-codegen-productization-e967`

## GAS Composition Gate — Self Review

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**（执行后端替换 + 工具面；不新增 profile enum / 不新增平行 opcode）

结论: **PASS**（设计切片；实现按 CG-0…CG-6）

一句话理由: 作者格式与 L0 IR 不变；Codegen 是同一 `GraphInstruction` 的可替换后端，语义金样仍是既有 handler，满足「禁止第二套作者格式 / 禁止平行 opcode」。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 全量 emitter | 0 执行后端 | Core `GraphCsharpEmitter`（升格自测试尖峰） |
| Roslyn/ALC 宿主 | 0 | `GraphCodegenCompilerHost` |
| 装载模式 | 2 配置 | game/mod 配置键 + Registry 挂 GeneratedExecute |
| Bridge 预览/对拍 | 3 工具 | Editor.Bridge API |
| 编辑器面板 | 3 | React Codegen 面板 |

### 3. Reuse list

- Handlers: `GasGraphOpHandlerTable` 作语义金样
- Queues / Systems: 既有 `Execute`/`ExecuteSlice`、Continuation、TriggerGraph 挂载
- Resolvers / Registries: `GraphProgramRegistry`、`IGraphRuntimeApi`、`GraphTextHeap`、coverage registry
- Existing: `LinearIntGraphCsharpEmitter` / `GraphRoslynAlcCompilerHost` 尖峰迁升

### 4. New Layer 0 ops (if any)

N/A — 不新增 GraphNodeOp；只为既有 op 增加发射策略。

### 5. Transaction boundary

Codegen 编译失败不得半替换入口；ALC 仅在成功后切换。产品 `codegen` 模式装载失败整图拒绝。

### 6. Config SSOT

行为配置落在: 既有 graph IR + 执行后端模式配置 + `graph_node_op_coverage.registry.json` 的 `codegenStatus`

是否新增 JSON schema: YES（coverage 字段扩展；后端模式键）— 不能用「再写一套 graphs-codegen.json 作者格式」代替。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「编不过静默解释」的产品默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / 覆盖登记 / 后端模式配置 — 不改 Core 行为开关 enum 冒充能力。
