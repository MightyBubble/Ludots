# GAS Composition Gate — Self Review

- **Task / Issue**: 实体模板组件组 uses 组装范式（#1574）
- **Date**: 2026-09-20
- **Agent / Author**: ZCode（分支 feat/entity-template-uses，基于 #1573 的 60eca2e3af）

## 1. Core judgment

新变体主要交付物是（A/B/C/D）: 均不是——交付物是**加载期配置组合语义**（模板表 `uses` 字段的装载期折叠：extends 父打底 → uses 块按序覆盖 → 自身最后）与**冲突可见性**（组件覆盖链记入既有 `ConfigConflictReport`）。不新增任何运行时行为变体、enum、preset 开关或平行管线。

结论: PASS

一句话理由: 能力拼装仍落在数据（templates.json 块条目 + uses 列表增行）上，折叠复用 #1573 同一展开器与 MergeIntoChild/JsonMerger 函数族，物化 SSOT 仍是 EntityBuilder 单轨，运行时行为面零变化。

## 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| uses 装载期折叠 | 3（authoring/config 组合） | `EntityTemplateInheritance`（既有展开器上的正交增量，两处加载终点共用） |
| 折叠优先级（后声明胜、自身最高） | 3 | 一次性私有克隆链 `FoldSources`（注册表共享的父/块对象全程只读） |
| 冲突可见性（覆盖链留痕） | 3 | `ConfigConflictReport.RecordComponentOverrideChain`（既有报告体系内新增最小方法） |

## 3. Reuse list

- Handlers: N/A（不触 effect handler）
- Queues / Systems: RuntimeEntitySpawnQueue / TemplateEntityBatchSpawner / MapLoader 原样消费折叠后模板对象，不改
- Resolvers / Registries: `DataRegistry<EntityTemplate>` + ConfigPipeline ArrayById 合并原样；`EntityTemplateKeys` 不变
- 折叠原语全复用 #1573: `MergeIntoChild`（components 字段级深合并 / children、TriggerGraphs 追加 / 标量非空才覆盖）、`TryConsumeReplaceMarker`（`__replace` 整替）、`CloneChild`
- Existing presets / graphs: 图侧 SpawnTemplate(op 447) 与 TriggerGraphs 挂载不动
- 环检测：既有 `expanding` 集合扩展为 extends+uses 混合图共用

## 4. New Layer 0 ops (if any)

N/A——无需新 atomic op；本任务是静态配置组合，不产生运行时事务。

## 5. Transaction boundary

N/A——折叠发生在装载期，失败即启动失败（fail fast），无运行时回滚面。

## 6. Config SSOT

行为配置落在: `Entities/templates.json`（既有目录登记表，ArrayById/id）；块是普通模板条目，被 uses 引用即为块，不建新表、不加 abstract 开关。

是否新增 JSON schema: NO——只给既有 EntityTemplate schema 增加一个可选字段 `uses`；不新建表、不新建 loader、不新建 profile。

## 7. Red flag scan

- [x] 未新增 profile inherit/placement enum（`uses` 是模板表 authoring 字段，非 runtime 声明式开关；运行时行为面零变化）
- [x] 未新建与 spawn 平行的物化管线（EntityBuilder 仍是唯一物化路径；批量快速路径消费折叠后对象）
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（未知块、uses+extends 混合环 → 启动失败，错误指明双方 id）
- [x] 未掩盖扁平组合的冲突（同一组件多源写入记入 ConfigConflictReport 覆盖链，`block.a -> block.b -> self`）

## 8. Next variant test

「下一个 Mod 变体」（新单位 = 可移动 + 可选中 + 有血条）将修改: **新增块条目与一行 uses 列表（纯数据）**——不改 Core enum、不改 handler、不加 preset 开关。
