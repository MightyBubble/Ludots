# GAS Composition Gate — #1306 交互模式组件 + 投影 + SetInteractionMode op

- **Task / Issue**: #1306 路线①②（InteractionMode entity 组件 + InputContextProjectionSystem + SetInteractionMode graph op）
- **Date**: 2026-08-28
- **Agent / Author**: codex/mode-entity-projection slice

## 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**（一个新 graph 节点 SetInteractionMode + 一个读取实体组件的投影 system；无 enum/开关/preset 变体）

结论: **PASS**

一句话理由: 模式切换的唯一写入口是单一职责的 Layer 0 op（写/删一个实体组件），mode→context 展开是明文数据表驱动的投影，下一个变体只改数据表与 graph 连线，不改 Core enum。

## 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 写/清除实体 InteractionMode 组件 | 0 | `GraphNodeOp.SetInteractionMode` handler → `GasGraphRuntimeApi.SetInteractionMode` |
| mode → context 集合展开 | 0（纯数据 + 查询） | `InteractionModeMap`（ConfigPipeline 加载 `Input/interaction_modes.json`） |
| context 集合 → Push/Pop 命令流 | 0（纯派生 system） | `InputContextProjectionSystem`（SystemGroup.InputCollection） |
| 玩法变体（哪个技能进哪个模式） | 2 | effect graph / trigger graph 连线（本切片不动） |

## 3. Reuse list

- Handlers: `GasGraphOpHandlerTable`（注册机制）、`GraphProgramSymbolPatcher`（symbol patch）、`GasGraphRuntimeApi`（Bind* 晚绑模式，照 `BindRuntimeEntitySpawn`）
- Queues / Systems: `SystemGroup.InputCollection` 注册（照 `SeatPossessionSyncSystem`）、`PlayerInputHandler.PushContext/PopContext`（不动本体）
- Resolvers / Registries: `StringIntRegistry`、`ConfigKeyRegistry`（graph symbol→mode 名）、`ClientLocalSeatRegistry`（seat→possessed rep）、`ConfigPipeline`（`RequireEntry` + `MergeDeepObjectFromCatalog`，照 `ControlSchemeConfigLoader`）
- Existing presets / graphs: 无需改既有 preset/graph

## 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| `SetInteractionMode` (opcode 462) | 把一个实体切到明文 mode（非默认 add/set 组件，默认 remove 组件），未知实体/未知 mode fail-fast 点名 | 无既有 op 写实体交互模式组件；WriteBlackboard* 只写黑板，SetWorldPosition 只写位置；模式是新的模拟状态载体，不能由现有 op 组合表达 |

## 5. Transaction boundary

必须原子 rollback 的步骤: 无。单实体单组件写入是幂等的最小单元；模式切换失败（未知实体/未知 mode）在写入前 fail-fast，不留半态。

## 6. Config SSOT

行为配置落在: `Input/interaction_modes.json`（ConfigCatalog 声明，DeepObject 合并；mode → [{contextId, priority}]，未定义 context / priority 与 IMC 定义不一致 fail-fast 点名）

是否新增 JSON schema: **YES**（一张新数据表）。理由：mode→context 映射是数据不是代码分支，#1306 明文判据要求它以明文配置存在；它不是 effect preset 变体开关，不进 `*_profiles.json` 家族。

## 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（无组件 = mode.normal 语义是 owner 拍板的稀疏合同，不是 fallback 分支）

## 8. Next variant test

「下一个 Mod 变体」将修改: **数据表 + graph 连线**（新 mode 加一行 JSON；新玩法模式切换改 effect graph），不改 Core enum、不改投影代码。
