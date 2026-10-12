## GAS Composition Gate — Self Review

- **Task / Issue**: L79 crowd unit spawn through RuntimeEntitySpawnRequest
- **Date**: 2026-10-12
- **Agent / Author**: L79 implementer

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 人群单位改走现有 RuntimeEntitySpawnQueue / RuntimeEntitySpawnSystem / EntityBuilder 与批车道；缺的批组件准入、逐实例初始化器、整批入队失败和空解析器归属都补在这条管线上。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 模板物化 | 0 | RuntimeEntitySpawnSystem.SpawnTemplate / TemplateEntityBatchSpawner.TryCreateBatch |
| 批组件准入 | 0 | TemplateBatchComponentAdmission + ComponentRegistry.Apply 探针 |
| 逐实例写入 | 0 | IRuntimeSpawnInstanceInitializer，挂在生成系统实例上，WriteChunk 不 Add |
| 销毁 | 0 | EntityLifecycleAtomicOps.ConsumeEntity |
| 人群占槽与回执绑定 | 2 | CrowdSimUnits 只组请求、读回执、维护稠密表 |

### 3. Reuse list

- Handlers: 无新 BuiltinHandler
- Queues / Systems: RuntimeEntitySpawnQueue、RuntimeEntitySpawnReceiptQueue、RuntimeEntitySpawnSystem、TemplateEntityBatchSpawner
- Resolvers / Registries: ComponentRegistry、EntityTemplateKeyRegistry、PresentationStableIdAllocator、CompiledPresenterBootstrapRegistry、OwnershipResolver（有解析器才建边）
- Existing presets / graphs: 无

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| N/A | 未新增 spawn/lifecycle op 名 | 准入登记、初始化器 kind、`Materialize` 是现有生成系统的入口；不另排空阶段队列 |

### 5. Transaction boundary

必须原子 rollback 的步骤: 无新事务壳。`Materialize` 不写阶段队列。物化中途失败沿用生成系统现有的预检/抛错。

### 6. Config SSOT

行为配置落在: effect template / graph / catalog（路径）: 单位组件形状仍是 Entities/templates.json，经 ComponentRegistry。队列容量仍是 presentation.runtimeEntitySpawnQueueCapacity。无头队列长度取 sim.maxUnits。

是否新增 JSON schema: NO

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: 登记自己的批组件，或在自己的系统实例上登记 IRuntimeSpawnInstanceInitializer。不改 Core enum。
