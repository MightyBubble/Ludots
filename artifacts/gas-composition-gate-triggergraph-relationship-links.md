## GAS Composition Gate — Self Review

- **Task / Issue**: 地图触发图与脚本图可以建边、断边，并询问已有边、度量、旗标。效果计划仍拒绝这些写节点。
- **Date**: 2026-09-24
- **Agent / Author**: cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 不新增节点、不新增枚举。已有建边、断边、问边节点放开到 Script 与 TriggerGraph 的作者面；效果计划继续把写节点标成未认证。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 建边 / 断边 / 问边 / 问度量 / 问旗标 | 2 | 已有图节点，图种掩码与策略放行 |
| 效果计划拒绝写节点 | 1 | 既有 Unsupported 元数据，不把关系边写进效果事务 |
| 落库 | 0 | 既有 RelationshipRuntime |

### 3. Reuse list

- Handlers: HandleRelationshipEnsureLink / RemoveLink / HasLink / GetMetric / HasFlag
- Queues / Systems: 无新队列。地图触发图仍走 GraphExecutor 脚本片
- Resolvers / Registries: RelationshipTypeRegistry、RelationshipRuntime
- Existing presets / graphs: 效果模板引用写节点时仍由 EffectExecutionPlanCompiler 拒绝

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

必须原子 rollback 的步骤: 无新增。关系边写入不进效果副作用事务；事务开着时运行时仍抛 GAS.EFFECT_TRANSACTION.ERR.UnsupportedSideEffect。效果计划编译仍抛 GAS.EFFECT_PLAN.ERR.UnsupportedOperation。

### 6. Config SSOT

行为配置落在: graph（关系类型符号仍在 Relationships catalog）

是否新增 JSON schema: NO

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线
