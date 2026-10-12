# GAS Composition Gate — L43 选中与下令

- **Task / Issue**: L43 群体选中退出指令日志，下令成员在提交时钉死
- **Date**: 2026-10-12
- **Agent / Author**: cloud agent

## 判断标准结论

新变体主要交付物是 **A**：已有 op 的连线（`RelationshipQueryOutgoing` → `QueryFilterTagNone` → `ScreenRegionToEntities` → `WriteCollection`；`selected` 或同一条 Owns 查询 → `SubmitCommandIntent`）加上 `OrderBufferSystem` 按订单类型把批次交给外部消费者。

结论: **PASS**

一句话理由: 选中与“全体本方”都走既有图 op；空选中查玩家代表的 Owns，隐藏模板用标签滤掉。`OrderBufferSystem` 把 `crowdSimulationMove` 整批交给群体会话，并写上 EntityIntake。

## GAS Composition Gate — Self Review

- **Task / Issue**: L43
- **Date**: 2026-10-12
- **Agent / Author**: cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 框选与下令是图连线。外部消费者是订单接入上的一种类型分发，不新开施法管线，也不新加 profile enum。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 框选命中 | 2 | `RelationshipQueryOutgoing`（Owns）+ `QueryFilterTagNone` + `ScreenRegionToEntities` + `WriteCollection` |
| 框选修饰键 | 2 | `SwitchInt` 按 `MapTrigger.Modifiers` 选 Replace / Add / Subtract |
| 下令成员 | 2 | `QueryFromCollection`（`selected`）或 Owns + 标签过滤 + `SubmitCommandIntent` |
| 意图排空与路由 | 0 | `CommandIntentBufferDrainSystem` + `CommandIntentProfileRegistry.RouteGroup` |
| 共享批次入队 | 0 | `OrderQueue.TryEnqueueSharedBatch` |
| 外部订单消费者 | 0 | `OrderBufferSystem.RegisterExternalOrderConsumer`：整批交给消费者，并写 EntityIntake |
| 隐藏模板 | 3 | 模板 `GameplayTagContainer` 带 `crowd_simulation.hidden_template` |
| 参考脚本翻译 | 3 | 对拍调用 `CrowdReferenceScript`；演示脚本用 `CrowdDemoScript` 补本方句柄 |

### 3. Reuse list

- Handlers: `RelationshipQueryOutgoing`、`QueryFilterTagNone`、`QueryFromCollection`、`ScreenRegionToEntities`、`WriteCollection`、`SubmitCommandIntent`、`SwitchInt`、`AggCount`、`BranchBool`
- Queues / Systems: `CommandIntentSubmissionBuffer`、`CommandIntentBufferDrainSystem`、`OrderQueue`、`OrderBufferSystem`、`CrowdCommandQueue`
- Resolvers / Registries: `CommandIntentProfileRegistry`、`OrderTypeRegistry`、`CollectionApplier`、`EntityCollectionStore`、`TagRegistry`
- Existing presets / graphs: Case E 框选修饰键开关；RTS `graph.rts.command_commit` 的空选中分支形状

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| 无新 graph op | | `RelationshipQueryOutgoing` 在要求完整结果时，按 `QueryFromCollection` 的方式把目标缓冲长大再收一次。截断查询（flags == 1）仍把丢下的条数交给调用方。 |

`OrderBufferSystem` 的外部消费者按订单类型 id 登记。队头连续的已登记批次在会话步进前交出去，并按实体接入的方式写 EntityIntake，使 GlobalIntake 不会留到下一步。未登记类型留在队列里，走原来的单位订单接入。

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。集合写入沿用 `CollectionApplier`。消费者在副作用之前校验；抛出时批次仍在队列里，EntityIntake 预约取消。

### 6. Config SSOT

行为配置落在: graph / catalog

- `mods/showcases/crowd_simulation/CrowdSimulationS4DeployMod/assets/GAS/graphs/graph.crowd_simulation.*.json`
- `mods/capabilities/navigation/CrowdSimulationMod/assets/GAS/order_types.json`（对象按键合并）
- `mods/capabilities/navigation/CrowdSimulationMod/assets/GAS/tag_rules.json`（`crowd_simulation.hidden_template`）
- `mods/showcases/crowd_simulation/CrowdSimulationS4DeployMod/assets/Input/interaction_context_profiles.json`
- `mods/showcases/crowd_simulation/CrowdSimulationS4DeployMod/assets/Input/command_intent_profiles.json`（只含 `intent.command.crowd_simulation`）
- `mods/showcases/crowd_simulation/CrowdSimulationS4DeployMod/assets/Input/default_input.json`

`command_intent_profiles.json` 的 `profiles` 是以档案 id 为键的对象，同键深合并。S4 只放群体档案。订单类型与标签按键合并，放在 CrowdSimulationMod。

是否新增 JSON schema: 否（`profiles` 从数组改成对象，加载器拒绝数组并写明要对象）

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线，或再登记一个订单类型消费者

## 复用 / 新增

| 类型 | 项 |
|------|-----|
| 复用 | CollectionApplier、EntityCollectionStore、RelationshipQueryOutgoing、QueryFilterTagNone、QueryFromCollection、ScreenRegionToEntities、WriteCollection、SwitchInt、SubmitCommandIntent、CommandIntentBufferDrainSystem、OrderQueue 共享批次 |
| 新增 Layer 0 | `OrderBufferSystem` 外部订单消费者 |
| 新增 Layer 2 | 群体框选图、Owns 下令图、`intent.command.crowd_simulation`、`crowdSimulationMove` |
| 禁止 | 选中字节、SelectionMirror、select 指令、花名册集合、内核里把空成员改写成全体 |
