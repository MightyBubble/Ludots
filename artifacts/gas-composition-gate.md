## GAS Composition Gate — Self Review

- **Task / Issue**: S13 · Script 方言拓宽 + L2 作者面（B19 / B20）
- **Date**: 2026-08-14
- **Agent / Author**: Cursor Grok 4.6

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 不新增 opcode、不放宽 kind、不加 profile enum；经 S12 `GraphOpDescriptorTable` 把已有读属性 / 黑板 / 查询 op 投影进 Script 作者面，L2 树与状态机用 JSON 组合既有 ActionLib 叶子，HFSM Yield 策略按宿主在加载期失败关闭。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| Script 可读属性 / 黑板 / 查询 | 0 | `GraphOpDescriptor` 的 `authorableKinds` + `scriptInputPorts`（投影前门） |
| Script 编译走已有线性发射 | 1 | `GraphControlFlowCompiler` 对非方言控制流 op 复用 `CompileLinearNode` |
| 行为树 / HFSM 数据作者面 | 2 | `AI/behavior_trees.json` + `AI/hfsm.json` + `GraphBehaviorDefinitionLoader` |
| 11 个动作名 SSOT | 2 | 仅 `GAS/action_lib.json` |
| 宿主 Yield 策略 | 0 | `GraphActionHostYieldPolicy`，`GraphActionCatalogLoader` 加载期校验 |

### 3. Reuse list

- Handlers: `GasGraphOpHandlerTable` 既有 `LoadAttribute` / `ReadBlackboard*` / `QueryRadius` / `AggCount`（Pure）
- Queues / Systems: 无新 system
- Resolvers / Registries: `GraphActionCatalog`、`GraphProgramRegistry`、`GraphIdRegistry`、`AiConfigCatalog` / `ConfigPipeline`
- Existing presets / graphs: S9 `GraphFrame`（Programs / F / E / Targets 已填）；S12 descriptor 表；既有 `BehaviorTreeWorld` / `HfsmWorld` 调度器

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| N/A | | |

禁止发明新 opcode。拓宽只改 descriptor 的 kind 掩码与 script 端口。

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。本票是作者面与装载期策略，不改 effect 事务壳。

### 6. Config SSOT

行为配置落在:

- `assets/Configs/GAS/action_lib.json`（11 个动作名唯一清单）
- `assets/Configs/AI/behavior_trees.json`（巡逻-追击-攻击树拓扑）
- `assets/Configs/AI/hfsm.json`（哨兵 Idle→Alert→Combat→Retreat）
- 覆盖表 `assets/Configs/GAS/graph_node_op_coverage.registry.json`（`authorableKinds` 由 descriptor 投影）

是否新增 JSON schema: YES — `behavior_trees.schema.json` / `hfsm.schema.json` 描述 L2 拓扑（节点连线 / 状态转移），不是 inherit/placement enum。下一个 Mod 变体改 JSON 连线，不改 Core enum。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（缺 host / 含 Yield 的 HFSM·Level 条目加载失败关闭；未知 ActionLib 名失败关闭）

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤

Mod 写自己的 `AI/behavior_trees.json` 与叶子 Script 图；不改 `GraphNodeOp`、不给工厂加参数。

### 复用 / 新增表

| 类型 | 项 |
|------|-----|
| 复用 | `GraphOpDescriptorTable`、`GraphControlFlowCompiler.CompileLinearNode`、`GraphActionCatalogLoader`、`AiConfigCatalog` / `ConfigPipeline`、`BehaviorTreeWorld` / `HfsmWorld`、S9 `GraphExecutor.ExecuteRegisteredSlice` |
| 新增 Layer 0 | 无 opcode；descriptor 为 Script 打开已有读/查 op |
| 新增 Layer 1 | 无 |
| 新增 Layer 2 | `GraphBehaviorDefinitionLoader` + 两份 AI JSON（Mod 可覆盖） |
| 禁止 | 新 opcode、放宽 kind、用工厂参数代替数据作者面、平行 descriptor 生成器 |

### HFSM Yield 裁决

**结论：HFSM 生命周期绑定与条件、以及 Level `RunScript`，不得挂含 Yield 的动作。Yield 只给 BehaviorTree ScriptSlice 与独立 Script 切片宿主。**

理由：HFSM 已有 think-wave 节拍，OnTick 本身就是「警戒一步」；Yield 再切片会与转移/OnExit 交错且合同未定义。实现与 `IHfsmGraphHost` 注释本来就要求 Halt。合同 §4.4「OnTick 内含 Yield」改为与实现一致，并把宿主维度校验从运行期挪到 `GraphActionCatalogLoader`。

## Graph Infrastructure Closeout — Self Review

- **Task / Issue**: Graph 基建收口：FuncLib / ActionLib 宿主约束与查询容量输出
- **Date**: 2026-08-14
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 本轮复用既有 graph opcode、VM、registry 和 catalog，只补已有查询参数的编译发射，以及已有 ActionLib/FuncLib 合同的宿主校验；没有新增 profile enum、preset 开关或第二套管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2) | 实现载体 |
|-----------|-----------------|----------|
| 查询 `AllowTruncated` 的 dropped 输出 | 2 | 既有 Query/Linear graph 编译器 + `GraphInstruction` 的 flags / scratch register |
| FuncLib 纯度和 Invoke 目标闭合 | 0 | `GraphYieldPurityValidator` + `GraphProgramRegistry.ValidateInvokeTargets` |
| ActionLib 宿主与 Yield 约束 | 2 | `GAS/action_lib.json` + `GraphActionCatalogLoader` + `GraphActionCatalog` |

### 3. Reuse list

- Handlers: `GasGraphOpHandlerTable` 的既有空间查询、关系查询和列表处理
- Queues / Systems: 无新增 system、queue 或 effect transaction
- Resolvers / Registries: `GraphProgramRegistry`、`GraphFunctionCatalog`、`GraphActionCatalog`、`GraphIdRegistry`、`GraphYieldPurityValidator`
- Existing graphs / tests: 既有 `GAS/graphs.json`、`GAS/func_lib.json`、`GAS/action_lib.json` 与 GasTests graph/AI 覆盖

### 4. New Layer 0 ops

N/A。没有新增 opcode；只让已有 opcode 的配置约束和编译结果一致。

### 5. Transaction boundary

无。本轮只改图编译、目录装载和调用宿主校验，不改变 effect transaction 的提交或回滚边界。

### 6. Config SSOT

行为配置落在：

- `GAS/graphs.json` 的 `queryCapacityPolicy` / `droppedOutput` 字段
- `GAS/func_lib.json`
- `GAS/action_lib.json`

是否新增 JSON schema: NO。使用已有 graph/catalog schema 表达组合与宿主约束，不引入新的 profile DSL。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加说不清的默认 fallback
- [x] 未新增第二套 graph VM 或作者前门

### 8. Next variant test

下一个 Mod 变体修改 graph 连线、查询容量策略或 catalog 条目，不修改 Core enum，不复制 loader/registry。

## PR #969 Graph Status SSOT Closeout — Self Review

- **Task / Issue**: PR #969 图能力唯一入口与 Graph 基建收口审计
- **Date**: 2026-08-15
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 本轮只收紧既有 Graph 合同、测试和 gitbook 现状页；程序显式 `HaltReturnInt`，非法 kind/op 走既有 registry 注册期拒绝，不新增 opcode、profile enum、preset 开关或第二套管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| Graph 程序显式结束合同 | 0 | `GraphProgramRegistry` / `GasGraphExecutor` 既有校验面 |
| Derived / Validation / Effect op 约束 | 0 | `GraphKindOperationPolicy` 与注册期负例测试 |
| Mod 图资源现状修正 | 2 | 既有 `GAS/graphs.json` 图连线 |
| 图能力进度入口 | 3 | `gitbook/architecture/graph-capability-status.md` |

### 3. Reuse list

- Handlers: `GasGraphOpHandlerTable` 既有 op
- Queues / Systems: 无新增 queue 或 system
- Resolvers / Registries: `GraphProgramRegistry`、`GraphKindOperationPolicy`、`ModRegistryAmbient`
- Existing presets / graphs: 既有测试 fixture、showcase graph JSON、FourX demo graph JSON

### 4. New Layer 0 ops (if any)

N/A。没有新增 opcode；只让既有图和测试遵守显式 Halt 与注册期拒绝合同。

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。本轮不改 effect transaction，只校准 graph 合同、loader/bootstrap 和文档进度。

### 6. Config SSOT

行为配置落在: 既有 `GAS/graphs.json` / showcase graph JSON / gitbook 图能力现状页。

是否新增 JSON schema: NO。没有新增 schema；所有变体仍通过 graph 连线和已有 catalog 表达。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加说不清的默认 fallback

### 8. Next variant test

下一个 Mod 变体修改: graph 连线 / effect 步骤。

### 9. Final closeout validation

- `dotnet test src\Tests\GasTests\GasTests.csproj --no-restore --filter "FullyQualifiedName~Graph"`: PASS，498/498。
- `dotnet test src\Tests\GasTests\GasTests.csproj --no-restore --filter "FullyQualifiedName~GraphBehaviorArenaAcceptanceTests.CombinedThinkWaves_60fpsCadence_AiEvery12Frames_StayUnderFiveMs"`: PASS，A=10_000 / N_topo=8 combined wave，avg 2.881ms，p95 3.440ms，over5ms=0。
- `dotnet test src\Tests\GasTests\GasTests.csproj --no-restore --filter "FullyQualifiedName~GraphBehaviorPressureMatrixTests.WritePressureMatrices_M1_M2_M3_M4_M5_M6"`: PASS；M1 的 N_topo≤32 是 15ms gate，N_topo=64 只保留压力探针。
- `python scripts\validate-registry.py`: PASS，错误 0，警告 23 个既有 screenshot warning。
- `pwsh .\scripts\validate-docs.ps1`: PASS。
