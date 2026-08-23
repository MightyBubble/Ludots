# GAS Composition Gate — RelationshipTemplate（关系类型出生模板）

本票自审（#1063）。不覆盖 `artifacts/gas-composition-gate.md` 正本。

## 任务摘要

catalog `types` 条目支持 `template.components`（组件字典 authoring），在 catalog 安装期通过既有
`ComponentRegistry.Apply` authoring 链烘焙成预编译补丁（装箱组件值 + 组件类型分类），物化点
`RelationshipRuntime.MaterializeRelationshipEntity` 在关系实体首次创建时应用补丁（AddRange/Set，
零 JSON 解析、零分配）；实体已存在则直接返回，不重放模板。

叙事关系（如 `Kinship.FatherSon`）自此可纯数据声明初始属性与出生标签，零代码。

## 判断标准结论

**通过（A）**

主要交付物不是新 enum / preset 开关 / 平行管线，而是：

1. 复用 `ComponentRegistry` 既有组件字典 authoring 链（`EntityTemplate.Components` →
   `ComponentRegistry.Apply` 的同一机制），只是把入口开进关系 catalog；
2. 在唯一物化点（`MaterializeRelationshipEntity`，已存在）按首创建语义应用预编译补丁。

组件装配机制零新增；物化管线零新增。

## GAS Composition Gate — Self Review

- **Task / Issue**: #1063 RelationshipTemplate：关系类型初始属性/标签模板（物化时应用）
- **Date**: 2026-08-23
- **Agent / Author**: ZCode (GLM)

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**

结论: **PASS**

一句话理由: 模板 = 已有 authoring 链（ComponentRegistry 组件字典）+ 已有物化点（MaterializeRelationshipEntity）的数据化接入，不新增 profile enum、preset 开关或第二条物化管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 组件 JSON → ECS 组件（一次） | 0 | 既有 `ComponentRegistry.Apply`（含 `SetAttributeBuffer` 名字解析、新补 `SetGameplayTagContainer` 具名标签） |
| 模板烘焙（安装期，原型实体读回装箱值） | 0 | `RelationshipTypeTemplate.Bake`（新，单一职责：编译一次） |
| 关系实体物化（唯一物化点） | 0 | 既有 `RelationshipRuntime.MaterializeRelationshipEntity` |
| 关系类型模板声明 | 2 | `Relationships/catalog.json` `types[].template.components`（Mod 可改数据） |
| 示范叙事类型 | 3 | RelationshipShowcaseMod catalog 数据（`Kinship.FatherSon`） |

### 3. Reuse list

- Handlers: `ComponentRegistry` 全部既有 setter（`SetAttributeBuffer` 经 `AttributeRegistry` 名字解析；本票仅补 `GameplayTagContainer` 的具名 tag setter，属既有链内补能力，非平行机制）
- Queues / Systems: 无需触碰（物化在 `EnsureLink` 既有路径上）
- Resolvers / Registries: `RelationshipTypeRegistry`、`RelationshipMetricRegistry`（不动结构）、`AttributeRegistry`、`TagRegistry`（既有 GetId→Register 模式）、`RelationshipCatalogPipelineLoader` / `RelationshipCatalogInstaller.RegisterCatalog`（原地扩展）
- Existing presets / graphs: 无关（本票不涉及 effect/graph）
- ECS 既有非泛型 API：`World.AddRange(Span<object>)`、`World.Set(object)`、`Entity.Get(ComponentType)`、`World.GetArchetype(entity).Signature`（均已核实存在于本仓 Arch fork）

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| `RelationshipTypeTemplate.Bake/Apply` | 安装期把组件字典编译成补丁；物化期零 JSON 应用 | 无既有 op 承担「catalog 模板 → 关系实体出生组件」；Bake 只是对既有 ComponentRegistry setter 的一次性调用，Apply 只是对既有 Arch 非泛型 Add/Set 的调用 |

（无新 GAS op、无新 enum。）

### 5. Transaction boundary

模板应用发生在关系实体 `Create` 之后、登记 `_entityIndex` 之前，单线程安装/物化窗口内；失败即抛
（fail-fast），无部分应用回滚需求（未登记索引的孤儿实体会被重建索引的重复投影守卫暴露）。

### 6. Config SSOT

行为配置落在: `Relationships/catalog.json` `types[].template.components`（与 metrics/flags/bands/callbacks 同一 catalog，经同一 `RelationshipCatalogPipelineLoader` fragment 合并）。

是否新增 JSON schema: **YES（字段级增量，非新文件）** — 说明: `RelationshipTypeConfig` 增加 `Template` 字段（组件字典，与 `EntityTemplate.Components` 同构），走既有 loader/merge 管线；不通过组合表达的原因：模板本来就是本票要引入的声明维度，组件内容本身完全由既有 ComponentRegistry schema 组合表达，未发明任何新组件 DSL。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线（沿用 `MaterializeRelationshipEntity` 唯一物化点；烘焙用一次性原型实体，非持久 spawn 管线）
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（无模板 → 补丁为空 → 行为与现状逐字节一致；实体已存在 → 返回既有实体，不重放）
- [x] 模板不越权：`RelationshipInstanceCm`（身份组件）拒绝 authoring，fail-fast

### 8. Next variant test

「下一个 Mod 变体」（例如 `Kinship.SwornBrothers`、`Loyalty.Minister`）将修改: **catalog JSON 数据**
（types[].template.components 字典内容）。不动 Core enum、不动 handler、不动物化代码。

若选了 Core enum → FAIL（本票未选）。

## 热路径纪律（本票合同附加项）

- 模板编译一次，发生在 catalog 安装期（`RelationshipRuntime.InstallTypeTemplates`）；
- 物化期零 JSON 解析：补丁 = 安装期装箱的组件值数组 + 逐组件 `World.Add(object)`/`World.Set(object)`
  非泛型调用（Arch `Chunk.Copy(index, object)` 经 `Array.SetValue` 拷贝值，无每次装箱分配）；
- **实现期发现（影响选型）**：Arch `World.AddRange(Span<object>)` 内部按全局组件注册表大小做
  `stackalloc` bitset；进程内组件类型注册数增长后（测试进程加载全部 mod 引用，~3400+ 个组件位），
  循环内的动态大 stackalloc 会被 JIT 堆化，实测每次调用分配 ~432B。因此 Apply 用逐组件
  `Add(object)`（走缓存 add-edge，零 stackalloc），实测预热后 0 分配；该行为属 Arch 既有实现，
  非本票引入（已用隔离基准在干净对照下复现确认）。
- 无模板类型的热路径开销 = 一次数组长度判断（`_typeTemplates` 为空数组）；
- 既有零分配 churn 测试（`RelationshipRuntime_TypedEdgeChurnOnExistingPair_AllocatesZeroAfterWarmup`）
  不回归，并新增带模板类型的 churn 零分配测试（`TypeTemplate_TypedEdgeChurnOnTemplatedType_AllocatesZeroAfterWarmup`）。

## 复用 / 新增清单（§4.2 合并）

| 类型 | 项 |
|------|-----|
| 复用 | `ComponentRegistry.Apply` + 全部 setter；`EntityTemplate.Components` 字典形态；`RelationshipCatalogPipelineLoader`（fragment 合并）；`RelationshipCatalogInstaller.RegisterCatalog`；`RelationshipRuntime.MaterializeRelationshipEntity`；`AttributeRegistry`/`TagRegistry` 名字解析；Arch `Add(Entity, object)`/`Set(Entity, object)`/`Get(Entity, ComponentType)`/`GetArchetype(Entity).Signature` |
| 新增 Layer 0 | `RelationshipTypeTemplate`（Bake/Apply，单一职责）；`RelationshipRuntime.InstallTypeTemplates`（安装期入口）；`ComponentRegistry.SetGameplayTagContainer`（既有链内补具名 tag authoring，替换原先无法表达内容的泛型反序列化注册） |
| 新增 Layer 1 | N/A（无事务需求） |
| 新增 Layer 2 | catalog `types[].template` 字段（数据） |
| 禁止项核对 | 未新建平行组件装配机制（走 ComponentRegistry）；未新建第二条物化管线（走 MaterializeRelationshipEntity）；未动 metrics/flags 全局注册表、ChangeBuffer/band/callback 语义；未动 isSymmetric 语义（不物化镜像边）；未碰 #1064 attachment 线文件；未新增 `docs/adr/` 文件；未改 gitbook SSOT 页面 |

## 边界遵守

- Core catalog（`assets/Relationships/catalog.json`）不动；示范类型进 RelationshipShowcaseMod（现有
  showcase mod catalog 增量）。
- 模板继承/引用（基模板特化）二期，本票不做。
- Reserved 三名（Owns/Controls/MemberOf）与 RFC-0065 语义不动。
