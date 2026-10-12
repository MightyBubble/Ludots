# GAS Composition Gate — L43 选中与下令

- **Task / Issue**: L43 群体选中退出指令日志，下令成员在提交时钉死
- **Date**: 2026-10-12
- **Agent / Author**: cloud agent

## 判断标准结论

新变体主要交付物是 **A**：已有 op 的连线（`QueryFromCollection` → `ScreenRegionToEntities` → `WriteCollection`；`QueryFromCollection` → `SubmitCommandIntent`）加上一条已有队列的消费（`OrderQueue` 按类型抽出 `crowdSimulationMove`）。

结论: **PASS**

一句话理由: 选中与名单都走集合仓和既有图 op；空选中的“全体本方”是花名册图在提交前查出成员，内核不再把空成员改写成全体。

## GAS Composition Gate — Self Review

- **Task / Issue**: L43
- **Date**: 2026-10-12
- **Agent / Author**: cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 框选与下令是图连线；`OrderQueue.DrainOrderType` 只是把已经入队的一种订单交还给它的消费者，不新开一条施法管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 框选命中 | 2 | `QueryFromCollection` + `ScreenRegionToEntities` + `WriteCollection` |
| 下令成员 | 2 | `QueryFromCollection`（`selected` 或 `crowd_simulation.roster`）+ `SubmitCommandIntent` |
| 意图排空与路由 | 0 | `CommandIntentBufferDrainSystem` + `CommandIntentProfileRegistry.RouteGroup` |
| 共享批次入队 | 0 | `OrderQueue.TryEnqueueSharedBatch` |
| 群体订单出队 | 0 | `OrderQueue.DrainOrderType`（只抽一种订单类型，其余留在队列） |
| 花名册入集 | 3 | 单位生成时 `CollectionApplier.Apply` 写入 `crowd_simulation.roster` |
| 参考脚本翻译 | 3 | `CrowdReferenceScript` 在指令入日志前把参考 select/order 译成带成员的 order |

### 3. Reuse list

- Handlers: `QueryFromCollection`、`ScreenRegionToEntities`、`WriteCollection`、`SubmitCommandIntent`、`AggCount`、`BranchBool`
- Queues / Systems: `CommandIntentSubmissionBuffer`、`CommandIntentBufferDrainSystem`、`OrderQueue`、`CrowdCommandQueue`
- Resolvers / Registries: `CommandIntentProfileRegistry`、`OrderTypeRegistry`、`CollectionApplier`、`EntityCollectionStore`
- Existing presets / graphs: RTS `graph.rts.select_hit` 与 Case E `graph.case_e.command_commit` 的连线形状

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| 无新 graph op | | 本方过滤放在花名册集合里：`QueryScreenRegionCollection` 的派生源只能是 `QueryAllMapEntities` 计划，而计划谓词没有“同一 PlayerOwner”。`ScreenRegionToEntities` 能过滤调用方已经交给它的名单，花名册用 `QueryFromCollection` 读出即可。同队关系会把玩家 1 与玩家 3 算进同一边，不能当本方。 |

（`OrderQueue.DrainOrderType` 不是 graph op。队列原先只能整批 `TryDequeueBatch`，群体订单若留在队列里会被下一帧重复提交，也会被 `OrderBufferSystem` 当成单位订单消化。）

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。集合写入沿用 `CollectionApplier` 的单次替换；意图提交沿用现有 drain。参考脚本翻译失败时指令不入日志。

### 6. Config SSOT

行为配置落在: graph / catalog

- `mods/showcases/crowd_simulation/CrowdSimulationS4DeployMod/assets/GAS/graphs/graph.crowd_simulation.*.json`
- `mods/capabilities/navigation/CrowdSimulationMod/assets/GAS/order_types.json`（对象按键合并）
- `mods/showcases/crowd_simulation/CrowdSimulationS4DeployMod/assets/Input/interaction_context_profiles.json`（profiles 追加）
- `mods/showcases/crowd_simulation/CrowdSimulationS4DeployMod/assets/Input/command_intent_profiles.json`
- `mods/showcases/crowd_simulation/CrowdSimulationS4DeployMod/assets/Input/default_input.json`

图、交互上下文和意图档案放在 S4 演示 Mod：`command_intent_profiles.json` 整段替换 `profiles`，引擎集成测试不加载 S4，因此不会改掉只加载 CrowdSimulationMod 时的 `intent.command.default`。S4 这份片段仍带上现行默认档案，再加 `intent.command.crowd_simulation`。订单类型按键合并，放在 CrowdSimulationMod。

是否新增 JSON schema: NO

`command_intent_profiles.json` 的数组合并策略是整段替换（目录项没有把 `profiles` 列入追加字段）。群体这份片段因此带上现行的 `intent.command.default`，再追加 `intent.command.crowd_simulation`。改目录追加策略会让已经整段替换默认意图的 Mod 出现重复 id。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线

## 复用 / 新增

| 类型 | 项 |
|------|-----|
| 复用 | CollectionApplier、EntityCollectionStore、QueryFromCollection、ScreenRegionToEntities、WriteCollection、SubmitCommandIntent、CommandIntentBufferDrainSystem、OrderQueue 共享批次、dispatch.all_together |
| 新增 Layer 0 | `OrderQueue.DrainOrderType` |
| 新增 Layer 2 | 群体框选图、花名册下令图、`intent.command.crowd_simulation`、`crowdSimulationMove` |
| 禁止 | 选中字节、SelectionMirror、select 指令、内核里把空成员改写成全体 |
