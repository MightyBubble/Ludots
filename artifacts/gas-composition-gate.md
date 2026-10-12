## GAS Composition Gate — Self Review

- **Task / Issue**: L79 crowd unit spawn through RuntimeEntitySpawnRequest
- **Date**: 2026-10-12
- **Agent / Author**: L79 implementer

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
| 销毁 | 0 | ConsumeEntity。不声明时一帧后收尾；DeclareNoConsumer 的世界同一次调用走 FinalizePublished |
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
