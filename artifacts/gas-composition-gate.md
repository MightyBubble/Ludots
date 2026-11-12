# GAS Composition Gate

集成同时保留两份自检：单位生成走 RuntimeEntitySpawnRequest，选中与下令走实体集合和订单桥。

## GAS Composition Gate — Self Review

- **Task / Issue**: crowd unit spawn through RuntimeEntitySpawnRequest
- **Date**: 2026-10-12
- **Agent / Author**: cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 人群单位仍走 RuntimeEntitySpawnSystem / EntityBuilder。`Materialize` 不排空阶段队列；批车道兼容的请求不论条数都走批车道，presenter 只来自直接引导。阶段队列的 `Update` 仍是多于一条才走批车道。销毁仍是 ConsumeEntity；没有呈现消费者的世界当场走现有收尾。批组件准入挂在生成器实例上，不另开管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 模板物化 | 0 | Update：多于一条走批车道，一条走单车道。Materialize：批车道兼容的请求不论条数都走批车道，presenter 只来自直接引导 |
| 批组件准入 | 0 | 生成器实例上的 TemplateBatchComponentAdmission；探针在临时世界，经 ComponentRegistry.Apply |
| 逐实例写入 | 0 | IRuntimeSpawnInstanceInitializer，挂在生成系统实例上，WriteChunk 不 Add |
| 行序 | 0 | 物化之后只交换已创建行；原型不是稠密集则抛错 |
| 销毁 | 0 | ConsumeEntity。不声明时一帧后收尾；DeclareNoConsumer 的世界同一次调用走 FinalizePublished。声明与生命周期系统互斥，不能撤销 |
| 人群占槽与回执绑定 | 2 | CrowdSimUnits 只组请求、调 Materialize、读回执、维护稠密表 |

### 3. Reuse list

- Handlers: 无新 BuiltinHandler
- Queues / Systems: RuntimeEntitySpawnQueue、RuntimeEntitySpawnReceiptQueue、RuntimeEntitySpawnSystem、TemplateEntityBatchSpawner
- Resolvers / Registries: ComponentRegistry、EntityTemplateKeyRegistry、PresentationStableIdAllocator、CompiledPresenterBootstrapRegistry、OwnershipResolver（有解析器才建边）
- Existing presets / graphs: 无

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| N/A | 未新增 spawn/lifecycle op 名 | `Materialize` 是现有生成系统的第二条入口，不是第二条物化管线，也不排空阶段队列。DeclareNoConsumer 是世界声明，不是新 op |

### 5. Transaction boundary

必须原子 rollback 的步骤: 无新事务壳。`Materialize` 不写阶段队列；直接物化产生的子请求留在调用侧，不进阶段队列。失败不回滚已创建的实体。还没物化的子请求会丢掉，异常写明条数；私有子请求表在调用结束时清空。

### 6. Config SSOT

行为配置落在: effect template / graph / catalog（路径）: 单位组件形状仍是 Entities/templates.json，经 ComponentRegistry。引擎阶段队列容量仍是 presentation.runtimeEntitySpawnQueueCapacity。人群不往这条队列里放单位。无头回执队列长度取 sim.maxUnits；无头请求队列只是系统构造所需，容量 16，单位不经它。

是否新增 JSON schema: NO

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: 在自己的生成系统实例上登记批组件或 IRuntimeSpawnInstanceInitializer。没有呈现消费者的世界调用 DeclareNoConsumer。不改 Core enum，不调用 RuntimeEntitySpawnSystem.Update 来排空别人的请求。


---

# GAS Composition Gate — 选中与下令

- **Task / Issue**: 群体选中退出指令日志，下令成员在提交时钉死
- **Date**: 2026-10-12
- **Agent / Author**: cloud agent

## 判断标准结论

新变体主要交付物是 **A**：已有 op 的连线（`RelationshipQueryOutgoing` → `QueryFilterTagNone` → `ScreenRegionToEntities` → `WriteCollection`；`selected` 或同一条 Owns 查询 → `SubmitCommandIntent`）加上 `OrderBufferSystem` 按订单类型把批次交给外部消费者。

结论: **PASS**

一句话理由: 选中与“全体本方”都走既有图 op；空选中查玩家代表的 Owns，隐藏模板用标签滤掉。`OrderBufferSystem` 把 `crowdSimulationMove` 整批交给群体会话，并写上 EntityIntake。

## GAS Composition Gate — Self Review

- **Task / Issue**: 群体选中退出指令日志，下令成员在提交时钉死
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

`OrderBufferSystem` 的外部消费者按订单类型 id 登记。排空在 `ProcessIncomingOrders`（AbilityActivation）。同一次排空里，已登记类型的批次都交给消费者，并按实体接入写 EntityIntake；单位订单照旧走实体接入。群体现场点击在下一拍会话步进执行。

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
