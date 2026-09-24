## GAS Composition Gate — Self Review

- **Task / Issue**: 效果图可以建边、断边、改度量、改旗标。这些写随效果提交落库，效果失败则撤回。地图触发图和脚本图里的建边、断边保持当场落库。
- **Date**: 2026-09-24
- **Agent / Author**: cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 不新增节点、不新增枚举。已有五件写节点改为可随效果事务提交；暂存和撤回挂在现有效果副作用事务上，落库仍走 RelationshipRuntime。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 建边 / 断边 / 改度量 / 改旗标 | 0 | 既有 RelationshipRuntime |
| 效果阶段暂存、提交、撤回 | 1 | EffectPhaseSideEffectTransaction + RelationshipLinkSideEffectJournal |
| 图种谁能写 | 2 | 既有图节点与图种掩码 |

### 3. Reuse list

- Handlers: HandleRelationshipEnsureLink / RemoveLink / SetMetric / AddMetric / SetFlag / HasLink / GetMetric / HasFlag；ApplyRelation.EnsureLink
- Queues / Systems: 现有 EffectPhaseSideEffectTransaction 的 Commit / Rollback
- Resolvers / Registries: RelationshipRuntime、RelationshipTypeRegistry、度量与旗标目录
- Existing presets / graphs: 效果模板引用这些写节点时计划种类为 GasTransactional

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

必须原子 rollback 的步骤: 同一效果阶段里的建边、断边、改度量、改旗标。提交前关系库不变，同图询问读暂存。提交时按顺序写入 RelationshipRuntime。若提交后后续步骤失败，按提交前的边快照恢复，并截断这次多出来的变更记录。

### 6. Config SSOT

行为配置落在: effect template / graph（关系类型、度量、旗标符号仍在 Relationships catalog）

是否新增 JSON schema: NO

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤
