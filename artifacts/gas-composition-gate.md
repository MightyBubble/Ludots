## GAS Composition Gate — Self Review

- **Task / Issue**: MightyBubble/Ludots#1540 切1（实体组模板 schema + 装载校验 + 地图摆放展开）
- **Date**: 2026-09-16
- **Agent / Author**: ZCode（分支 poi-entity-group-design，基准 origin/main f36773a2f5）

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**（组合既有 EntityTemplate 的作者面容器资产；物化路径完全复用 map 装载 lane）

结论: **PASS**

一句话理由: 组模板之于实体模板，等同 graph 之于 op——是组合层声明，不新增行为 enum/preset 开关，不新建第二条物化管线；展开产物是合成 EntitySpawnData，走既有实体循环（EntityBuilder/ComponentRegistry/TemplateBatchSpawner）。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 组模板 schema + 装载（Entities/groups.json，ArrayById） | 2（作者面数据） | `DataRegistry<EntityGroupTemplate>`（泛型装载器实例化，非平行 loader） |
| 装载期校验（localId 唯一 / template XOR group / 嵌套环） | 2 | 纯函数校验，先例 `ValidateTemplateChildrenGraph` |
| 组摆放展开 → 合成 EntitySpawnData | 2 | 纯配置层变换 `EntityGroupPlacement`，产出进既有实体循环 |
| 槽位 override 递归合并 | 0（既有） | `ConfigPipeline.DeepMerge`（JSON 组装层合并；组件层整组件替换合同不动） |
| 实体物化 | 0（既有） | MapLoader 单实体/batch lane 原样消费 |

### 3. Reuse list

- Handlers: 无新增（无 GAS handler 变更）
- Queues / Systems: `MapLoader.LoadEntitiesAndIndex` 既有循环消费展开结果；`TemplateBatchSpawner` 不改
- Resolvers / Registries: `DataRegistry<T>`、`ConfigPipeline.RequireEntry/MergeArrayByIdFromCatalog/DeepMerge`、`MapLoadEntityIndex`（前缀化 InstanceId 复用唯一性 fail-fast）
- Existing presets / graphs: `EntityTemplate`/`EntityTemplateLocalPose`（localPose 类型直接复用）

### 4. New Layer 0 ops (if any)

N/A——无新 op、无新 handler、无新 enum。

### 5. Transaction boundary

切1 无新事务边界：map 装载本就是同步 fail-fast lane（任一实体失败整图装载失败），组展开不改变该语义。组级原子性（组级 preflight + 整组回滚）属切3（RuntimeEntitySpawnQueue 侧），届时复用 `WriteCheckpoint/RollbackWrites` 既有事务设施。

### 6. Config SSOT

行为配置落在: `Entities/groups.json`（config_catalog.json 声明 ArrayById/id，Core+Mods 分片合并，与 Entities/templates.json 同合同）。

是否新增 JSON schema: **YES** — 组模板是多实体摆放的组合容器，组合对象（EntityTemplate、地图摆放）都在 graph/effect 之前的作者层，无法用 effect template（单实体语义）或 graph（运行时行为）表达；且它不携带行为开关，只有槽位声明与组件 JSON 合并。装载复用 `DataRegistry<T>` 泛型管线，不新建平行加载器。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线（展开产物走既有实体循环）
- [x] 未把 placement 校验塞进 lifecycle op（校验在装载期作者面）
- [x] 未添加「说不清的」默认 fallback（组摆放缺 InstanceId / 同时声明 template / 带 Overrides 一律 fail-fast）

### 8. Next variant test

「下一个 Mod 变体」（新 POI 组、改槽位组件、嵌套更深）将修改: **Entities/groups.json 数据**；无需触碰 Core enum。

### 附：护栏对照

- RFC-0065：组槽位的 Team/PlayerOwner 约束沿用 spawn 管线既有级联（多来源 Team 冲突即抛），切1 不放宽不收紧。
- MovementParticipation：组槽位是逻辑成员，允许移动单位（与 template child 的结构件禁令分线）。
- 确定性：展开序 = 地图声明序 × 槽位声明序 × 深度优先，无字典序依赖。
