# GAS Composition Gate — Self Review

- **Task / Issue**: 实体模板 extends 继承（authoring 增量组合）+ 实例 override 字段级深合并
- **Date**: 2026-09-20
- **Agent / Author**: ZCode（分支 feat/entity-template-inheritance）

## 1. Core judgment

新变体主要交付物是（A/B/C/D）: 均不是——交付物是**加载期配置组合语义**（模板表 `extends` 字段的装载期展开）与**物化期合并粒度**（EntityBuilder override 从整组件替换改为与模板组件的字段级深合并）。不新增任何运行时行为变体、enum、preset 开关或平行管线。

结论: PASS

一句话理由: 变体表达仍落在数据（templates.json 增行）上，物化 SSOT 仍是 EntityBuilder 单轨，图侧动态组合已有 SpawnTemplate(op 447) 覆盖，本任务不触 GAS 运行时行为面。

## 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 模板 extends 装载期展开 | 3（authoring/config 组合） | `EntityTemplateInheritance`（新，静态工具，两处加载终点共用） |
| 实例 override 字段级合并 | 0（物化 SSOT 内的语义修正） | `EntityBuilder.Build`（既有，单点） |
| 离线烘焙侧同语义 | 3 | `NavObstacleAuthoringCatalog.LoadMergedTemplates` 接入同一展开器 |

## 3. Reuse list

- Handlers: N/A（不触 effect handler）
- Queues / Systems: RuntimeEntitySpawnQueue / TemplateEntityBatchSpawner / MapLoader 原样消费展开后模板对象，不改
- Resolvers / Registries: `DataRegistry<EntityTemplate>` + ConfigPipeline ArrayById 合并（跨 mod 同 id 深合并复用）；`JsonMerger.Merge`（字段级深合并复用）；`EntityTemplateKeyRegistry` 不变
- Existing presets / graphs: 图侧 SpawnTemplate(op 447) 与 TriggerGraphs 挂载不动
- 语义先例：presenter `extends`（`PresenterDefinitionConfigLoader.ExpandDefinition`：递归展开、环检测 fail fast、rules/children 追加、标量子代胜）

## 4. New Layer 0 ops (if any)

N/A——无需新 atomic op；本任务是静态配置组合，不产生运行时事务。

## 5. Transaction boundary

N/A——展开发生在装载期，失败即启动失败（fail fast），无运行时回滚面。

## 6. Config SSOT

行为配置落在: `Entities/templates.json`（既有目录登记表，ArrayById/id）

是否新增 JSON schema: NO——只给既有 EntityTemplate schema 增加一个可选字段 `extends`；不新建表、不新建 loader、不新建 profile。

## 7. Red flag scan

- [x] 未新增 profile inherit/placement enum（`extends` 是模板表 authoring 字段，非 runtime 声明式开关；运行时行为面零变化）
- [x] 未新建与 spawn 平行的物化管线（EntityBuilder 仍是唯一物化路径；批量快速路径消费展开后对象）
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（未知父模板、继承环 → 启动失败，错误指明模板 id）

## 8. Next variant test

「下一个 Mod 变体」（新英雄 = 小兵 + 额外组件/数值/图）将修改: **新增一行 extends 模板（纯数据）**——不改 Core enum、不改 handler、不加 preset 开关。
