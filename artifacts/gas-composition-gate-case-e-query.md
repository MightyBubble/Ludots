## GAS Composition Gate — Self Review

- **Task / Issue**: Case E query completeness and derived index
- **Date**: 2026-09-07
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A（新增查询原语/已有图连线）

结论: PASS

一句话理由: 复用查询图和集合存储，增加绑定与空间筛选原语；不新增 profile 字段、语义组件或平行 DSL。

### 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|---|---:|---|
| ECS 完整收集 | 0 | EntitySetQueryRuntime 与引擎缓冲 |
| 派生成员维护 | 0 | DerivedEntityIndex 与 World 变更通知 |
| 图结果发布 | 2 | BindQueryCollection / QueryScreenRegionCollection / WriteCollection |

### 3. Reuse list

- Handlers: 现有 Query、WriteCollection
- Queues / Systems: DirtyEntityQueue、SpatialPartitionUpdateSystem
- Resolvers / Registries: EntityTemplateKeyRegistry、GraphProgramRegistry、EntityCollectionStore
- Existing graphs: Case E roster/box-hit

### 4. New Layer 0 ops

| Op 名 | 单一职责 | 为何不能组合现有 op |
|---|---|---|
| ECS complete collect | 从 chunk 生成完整候选结果 | 现有 TargetList 只支持 256 窗口 |
| BindQueryCollection | 把已有查询图绑定为集合源 | 原有集合没有依赖字段驱动的成员维护 |
| QueryScreenRegionCollection | 对绑定集合进行空间粗筛与屏幕精检 | 原有 ScreenRegionToEntities 逐个投影整个候选集 |

### 5. Transaction boundary

索引按地图释放，持有者死亡解除集合绑定。图程序替换后旧索引读取报错。普通查询缓冲按执行嵌套层级隔离，跨 Yield 结果由执行游标保存。本次没有实现 queryBegin/page/end 操作。

### 6. Config SSOT

行为配置落在 graph 查询计划和现有集合键；不新增 JSON schema。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加默认 fallback

### 8. Next variant test

下一个 Mod 变体修改 graph 连线或 effect 步骤。
