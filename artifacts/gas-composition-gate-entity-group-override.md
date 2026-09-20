## GAS Composition Gate — Self Review

- **Task / Issue**: #1540 切A——EntityTemplate 递归 children + localId 路径 + attach 标记统一模型；实体组即普通模板
- **Date**: 2026-09-16
- **Agent / Author**: user audit（pi 开发，用户审）

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A（把既有 `EntityTemplate` 升格为唯一本位，在其上长出递归 children / localId 路径 / attach 标记），实体组是普通模板的另一种写法，**不新增模板类型、不引入组模板 / prefab / 槽位表**。

结论: PASS

一句话理由: 没有第二套实体物化对照物——地图装载走 MapLoader DFS 全展，运行时出生复用既有 spawn 队列；只在 EntityTemplate 数据确权上加 children 递归与路径身份，别的都挂在既有原语上。

### 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|-----------|-------|----------|
| 递归 children 装载校验 | 0 小补丁 | MapLoader（ValidateChildNodes / DetectInlineChildrenCycle / SpawnTemplateChildNodes） |
| 实例本地路径表 | 0 小补丁 | MapLoadEntityIndex（RegisterLocalPath / TryGetByLocalPath，切片F 基石） |
| 实例差异 override（自身） | 0 数据确权 | EntityBuilder.WithMergedOverride（根组件整替换，deep-merge 于 config 片段层） |
| 可址路径错误快反 | 0 小补丁 | 缺 instanceId 且带可址后代 → fail-fast（S1-3） |
| 运行时出生（切G 预留） | 2 | RuntimeEntitySpawnSystem.EnqueueTemplateChildren 递归全展（S1-1）+ attach:false fail-fast（S1-2） |

### 3. Reuse list

- 模板: 仅 `EntityTemplate`（唯一类型）；id 进入既有 templates registry
- 展开序: 树深度优先 × 声明序（数组序，non dict）
- Overrides: 复用既有实例 overrides（根组件整替换）+ 路径 override（切B 后代 deep-merge，**不是第二套模板**）
- 可址路径: MapLoadEntityIndex（instanceId 全局名 + localId 局部名，两段不归一化）

### 4. New Layer 0 ops

N/A（不改 opcode；只把 children 从"一层位姿"升为"递归+路径身份"）

### 5. Transaction boundary

无新事务壳；RuntimeEntitySpawnRequest 单实体语义保留，children 由 spawn 队列自消费（幂等、无环）

### 6. Config SSOT

- `Entities/templates.json`：递归 children（localId / attach / 可选递归 / overrides）
- `Maps/*.json`：复用 `instanceId + template + position` 摆放语法，无新增 `group` 字段
- 可寻址路径：`instanceId + "." + localId链`（两段非空校验）

### 7. Red flag scan

- [x] 未新增第二种模板类型 / prefab / 槽位表
- [x] 未新建与地图装载平行的第二套物化管线（运行时只复用 spawn 队列）
- [x] 未在出生时给 attach:false 静默错误语义（合前 fail-fast，切E 落地）
- [x] 未添加静默 fallback（缺 instanceId 且带可址后代 fail-fast；路径不存在抛）
- [x] 两 POI 共用模板不归一化 world-id，靠实例前缀天然隔离

### 8. Next variant test

「下一个 Mod 变体」将修改: 针对**嵌套可复用组**写断言，验证运行时出生也能全层展开、父先于子、路径两段可址；待切B 后给后代 overridePaths 加抗性测试。
