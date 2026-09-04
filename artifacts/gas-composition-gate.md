# GAS Composition Gate — M3 末 ECS 溶解第一片：SangoEntityMirror（读模型桥）

> 本波自审按 `skills/governance/ludots-gas-composition-gate/SKILL.md` 产出（正式位置 `artifacts/gas-composition-gate.md`）。
> 前一份内容（#1030 graph debug stream，2026-08-24）已在 git 历史（dd4c398ba 之前数版），此处按当前任务覆盖。

## GAS Composition Gate — Self Review

- **Task / Issue**: Sango 移植 M3 末第一片——把内核 88 城/851 武将/存活部队镜像为 Arch ECS 读模型实体（SangoEntityMirror），回合边界 + 命令漏斗同步，内核事件驱动生命周期
- **Date**: 2026-09-04
- **Agent / Author**: M3.h（移植流水线）

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**（更准确地说：**不新增任何 GAS 变体**——本波是镜像实体 spawn/despawn（读模型），不新增 GAS effect/preset/graph op/profile schema）

结论: **PASS（组合闸门红线不适用；实体生命周期走 atomic-op 合同）**

一句话理由: 本波零改动 `BuiltinHandlerId`/`EffectPresetType`/graph op/`*_profiles.json`/morph DSL；镜像实体的物化复用 Layer 0 原子 op `EntityLifecycleAtomicOps.MaterializeTemplate`（EntityBuilder + 模板注册表 + WorldPositionCm 施加 + presenter bootstrap no-op），despawn 走非表现实体分支（`world.Destroy`，与 `RollbackMaterializedTarget` 的无 PresentationStableId 分支同构），不新建第二条物化管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 镜像实体物化（spawn） | 0（复用） | `EntityLifecycleAtomicOps.MaterializeTemplate(services, Entity.Null, "sango.mirror.{city\|person\|troop}", posCm)`；模板经 `SangoSimMod/assets/Entities/templates.json` 进引擎既有 `MapLoader.TemplateRegistry`（全 mod 合并装载，无平行加载器） |
| 镜像实体销毁（despawn） | 0（同构） | 镜像实体无 `PresentationStableId`（`MaterializeTemplate` 语义即剥离）→ `ConsumeEntity` 不适用，走 `RollbackMaterializedTarget` 同款非表现分支 `world.Destroy` |
| 数据面（SangoIdentity/ForceRef/StatsSnapshot） | 2（Mod 数据） | SangoSimMod 组件 struct，投影读取内核 PONO，无 JSON schema |
| 同步时机 | 2（复用既有漏斗） | 回合边界 + 命令生效 = `SangoCommandJournal.Record` 唯一漏斗（step 命令覆盖一切 AdvanceTurn 调用方）；结构/摆位 = 内核 GameEvent 群 |
| 每帧守卫（内核世界替换对账） | 2 | `SangoEntityMirrorSystem`（ISystemRegistrar 正式面，SystemGroup.Cleanup） |

### 3. Reuse list

- Handlers: 不动任何 GAS handler；复用 `EntityLifecycleAtomicOps.MaterializeTemplate`（Layer 0 公共静态 op）。
- Queues / Systems: 不新增队列；`SangoEntityMirrorSystem` 注册进引擎既有 `SystemGroup.Cleanup`（`context.Systems.RegisterSystem`，ISystemRegistrar 正式面）。
- Resolvers / Registries: `GameEngine.MapLoader.TemplateRegistry` / `MapLoader.EntityTemplateKeys`（模板装载既有合并链）、`CoreServiceKeys.PresentationStableIdAllocator` / `TagOps` / `PresenterEntityRuntime` / `PresenterDefinitionRegistry`（引擎服务，构造 `EntityLifecycleRuntimeServices`）。
- Existing presets / graphs: 不新增、不修改。读取面与 `SangoWorldTopics.ProjectCities` 同源（内核 PONO 同一批字段：gold/food/population/allPersons.Count/mBelongForce/mFlag.color/loyalty/Command/Strength/Intelligence/Politics/state/troops/morale/missionType），不新开读取路径。

### 4. New Layer 0 ops (if any)

N/A——无新增 op。镜像物化 = 现有 `MaterializeTemplate` + 三张最小模板（`sango.mirror.city/person/troop`，组件仅 `Name`，位置由 op 参数施加）。

### 5. Transaction boundary

无需 all-or-nothing rollback 的步骤：镜像实体是**只读投影**，单实体物化失败即抛错（fail-fast，模板缺失 `RequireTemplate` 已有该语义）；镜像不存在"部分镜像可回滚"的业务事务——内核才是真相源，镜像随时可全量重建（`RebuildAll`）。读模型纯净性由验收 c（WorldDigest 双跑逐位相等）证明。

### 6. Config SSOT

行为配置落在: 模板数据 `mods/sango/SangoSimMod/assets/Entities/templates.json`（经引擎全局 `Entities/templates.json` catalog 合并装载）；同步/生命周期行为是代码合同（事件订阅表），无行为型 JSON。

是否新增 JSON schema: **NO**（模板走引擎既有 EntityTemplate schema 与 config_catalog 既有条目，仅新增数据行）。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线（复用 MaterializeTemplate；模板批装载走 MapLoader 既有链）
- [x] 未把 placement 校验塞进 lifecycle op（镜像摆位来自内核格坐标投影，非 gameplay placement）
- [x] 未添加「说不清的」默认 fallback（服务缺失/模板缺失即类型化抛错；白城 ForceRef=0 是内核语义非 fallback）

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线 / effect 步骤**（后续片若把城金粮叠为 GAS attribute，走 `AttributeBuffer` + 既有 attribute mutation 通道；若 presenter 标记改挂镜像实体，走 presenter 定义/presenter 挂接数据）。本波未触碰 Core enum。

### 附：本波明确不做（边界）

- 不动 `src/`（引擎）、registry/launcher/gitbook、内核 `Game/`；
- 不新增 GAS effect/preset/graph op；
- `artifacts/gas-composition-gate.md` 是本波唯一 artifacts/ 变更；
- journal 埋点随 SangoOps 迁移是 M3 末后续片（本波只在 `SangoCommandJournal`（SangoRuntime，非内核）暴露一个命令生效观察事件供镜像订阅，不迁移埋点）。
