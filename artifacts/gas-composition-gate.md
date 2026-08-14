﻿## GAS Composition Gate — Self Review

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
