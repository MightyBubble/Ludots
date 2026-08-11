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

## GAS Composition Gate — Self Review (Track A: BranchBool / SwitchInt sugar)

- **Task / Issue**: Track A — Script CF authoring sugar BranchBool (complete) + SwitchInt (new); lower to existing L0 Jump / JumpIfFalse / CompareEqInt
- **Date**: 2026-08-11
- **Agent / Author**: Cloud Agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A — 既有 L0 op 的编译期连线糖（非新 GraphNodeOp enum）

结论: PASS

一句话理由: BranchBool / SwitchInt 仅在 `GraphControlFlowCompiler` 降为 JumpIfFalse+Jump（Switch 另加 ConstInt+CompareEqInt），不新增 L0 opcode、不平行 VM、不改 Effect/Score/Query Yield 合同。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| L0 跳转/比较 | 0 | 既有 `Jump` / `JumpIfFalse` / `CompareEqInt` / `ConstInt` |
| 作者糖 | 1 | `GraphControlFlowCompiler` AuthoredOpKind（非 GraphNodeOp） |
| 端口合同 | 1 | `GraphControlFlowPorts`（true/false；selector + case:{n} + default） |
| 验收 | 3 | GasTests ci-gate（Branch / Switch） |

### 3. Reuse list

- Handlers: `GasGraphOpHandlerTable` 既有 Jump / JumpIfFalse / CompareEqInt / ConstInt
- Queues / Systems: 无
- Resolvers / Registries: `GraphProgramRegistry`（测试执行）
- Existing presets / graphs: `GraphControlFlowDocument` + 既有 BranchBool 糖路径

### 4. New Layer 0 ops (if any)

N/A — 禁止新增 switch/branch opcode

### 5. Transaction boundary

必须原子 rollback 的步骤: 无（纯编译期糖；运行时仍单次 Script slice）

### 6. Config SSOT

行为配置落在: graph ControlFlow 文档（Script）

是否新增 JSON schema: NO — 复用 controlEdges/valueEdges；case 值编码在控制端口名 `case:{int}`

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（缺 default / 无 case / 非法端口 / 重复 case 值均 fail-closed）

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线

### Reuse / 新增表（§4.2）

| 类型 | 项 |
|------|-----|
| 复用 | GraphControlFlowDocument、GraphControlFlowCompiler、BranchBool、Jump/JumpIfFalse/CompareEqInt/ConstInt |
| 新增 Layer 0 op | 无 |
| 新增 Layer 1 | SwitchInt 编译期糖（AuthoredOpKind）+ case/default 端口约定 |
| 新增 Layer 2 | 无 |
| 禁止 | Yield/while（Track B）、Roslyn/codegen、平行 VM、新 GraphNodeOp |


## GAS Composition Gate — Self Review (A+B integrate)

- **Task / Issue**: Integrate Track A (BranchBool/SwitchInt) + Track B (Wait/While/Until) on #859 CF baseline
- **Date**: 2026-08-11
- **Agent / Author**: Cursor cloud agent (parent)

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A — 合并两轨作者糖，统一 `AuthoredOpKind` 编号，不新增 L0 opcode

结论: PASS

一句话理由: 整合仅解决 A/B 共享编译器冲突（`SwitchInt=2` vs `While=2` → SwitchInt=2/While=3/Until=4），硬化非 Script 糖的 fail-closed 重置；不新增 GraphNodeOp、不平行 VM、Effect 仍禁 Wait/Yield。

### 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|-----------|-------|----------|
| Branch/Switch/While/Until 糖 | 1 | GraphControlFlowCompiler AuthoredOpKind |
| Wait→Yield | 1→0 | 作者别名到既有 Yield |
| 端口 | 1 | true/false/selector/case/default/body/next |

### 3. Reuse list

- Track A + Track B 既有测试与糖降级路径
- GraphKindOperationPolicy Yield=Script-only

### 4. New Layer 0 ops

N/A

### 5. Transaction boundary

编译失败不产出 program；Effect 禁 Yield 不变

### 6. Config SSOT

GraphControlFlowDocument；无新 JSON schema

### 7. Red flag scan

- [x] 未新增 profile enum
- [x] 未平行物化管线
- [x] AuthoredOpKind 编号冲突已消除
- [x] 非 Script 使用糖时重置为 None（fail-closed）

### 8. Next variant test

改 Script 连线；不改 Core enum
