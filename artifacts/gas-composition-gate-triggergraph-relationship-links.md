## GAS Composition Gate — Self Review

> 效果图的写与撤回见 `artifacts/gas-composition-gate-effect-relationship-writes.md`。下面这段只保留地图触发图和脚本图当场落库的结论。

- **Task / Issue**: 地图触发图与脚本图可以建边、断边，并询问已有边、度量、旗标。效果图里的写节点另见效果事务暂存。
- **Date**: 2026-09-24
- **Agent / Author**: cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 不新增节点、不新增枚举。已有建边、断边、问边节点放开到 Script 与 TriggerGraph 的作者面。效果图里的提交与撤回不在本页，见效果事务自审。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 建边 / 断边 / 问边 / 问度量 / 问旗标 | 2 | 已有图节点，图种掩码与策略放行 |
| 地图触发图与脚本图当场落库 | 0 | 既有 RelationshipRuntime，这两张图没有效果事务 |
| 落库 | 0 | 既有 RelationshipRuntime |

### 3. Reuse list

- Handlers: HandleRelationshipEnsureLink / RemoveLink / HasLink / GetMetric / HasFlag
- Queues / Systems: 无新队列。地图触发图仍走 GraphExecutor 脚本片
- Resolvers / Registries: RelationshipTypeRegistry、RelationshipRuntime
- Existing presets / graphs: 效果模板里的写节点见 `artifacts/gas-composition-gate-effect-relationship-writes.md`

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

必须原子 rollback 的步骤: 地图触发图和脚本图的建边、断边不进效果事务，当场落库。效果图里的撤回见效果事务自审。

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
