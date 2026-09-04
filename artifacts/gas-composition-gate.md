# GAS Composition Gate — D-1' 城域原生重写(正经用 Ludots 第一波)

> 本波自审按 `skills/governance/ludots-gas-composition-gate/SKILL.md` 产出(正式位置 `artifacts/gas-composition-gate.md`)。
> 上一份内容(M3.h ECS 读模型桥,2026-09-04)见 git 历史(c1f938800),此处按当前任务覆盖。

## GAS Composition Gate — Self Review

- **Task / Issue**: Sango 移植 M3 末城域溶解——sango.city 模板升级(镜像→原生组件+GAS 属性)、城域玩法(经济公式/俸给军粮/内政 AI 命令/城陷面)以引擎系统原生实现、命令面改原生路径、digest 城域行双源可切、对拍验收(同种子同命令流逐位相等)
- **Date**: 2026-09-04
- **Agent / Author**: D-1'(移植流水线,城域原生重写)

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**（城数值走既有 GAS 属性通道:注册 attribute id + `AttributeMutationOps.SetBase` 写入,零新 effect/preset/graph op/profile schema;城实体物化沿用 M3.h 已过闸的 `MaterializeTemplate` Layer 0 op,模板仅加数据行）

结论: **PASS（属性通道与实体生命周期全部复用既有正式面,无新 GAS 变体）**

一句话理由: 本波零改动 `BuiltinHandlerId`/`EffectPresetType`/graph op/`*_profiles.json`/morph DSL;城数值 = 既有 AttributeRegistry(RegisterAttribute)+ AttributeBuffer + AttributeMutationOps.SetBase 正式通道;物化/销毁同 M3.h 合同(MaterializeTemplate / 非表现 world.Destroy),不建第二条物化管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 城实体物化(spawn) | 0（复用） | `EntityLifecycleAtomicOps.MaterializeTemplate(services, Entity.Null, "sango.city", posCm)`;模板 `SangoSimMod/assets/Entities/templates.json`(引擎既有合并装载链,仅加 `sango.city` 数据行:Name+AttributeBuffer+DirtyFlags) |
| 城实体销毁(despawn) | 0（同构,M3.h 合同) | 非表现实体分支 `world.Destroy` |
| 城 GAS 属性(sango.city.gold/food/population/durability) | 2(数据面,复用引擎通道) | `context.Registries.RegisterAttribute` 注册;值写入 `AttributeMutationOps.SetBase`(DirtyFlags+TagOps 走模板/服务);读取 `AttributeBuffer.GetCurrent` |
| 保序名单/内政 job 状态/AI 计划组件 | 2(Mod 数据) | SangoSimMod unmanaged struct 组件(InlineArray 保序,自动进 world.bin 序列化发现链) |
| 经济结算/俸给军粮/内政 AI 命令/城陷面 | 2(Mod 行为) | 引擎系统注册 `ISystemRegistrar`(结算=SystemGroup.Cleanup,城 AI=SystemGroup.InputCollection,以 src/Core 真实分组为准:引擎无专用回合/AI 组,Cleanup=仿真后对账相位、InputCollection=UtilityAiThinkScheduleSystem 所在 AI 相位);结算时点挂内核既有 GameEvent 面(OnCityMonthStart/OnCitySeasonStart/OnCityCalculateHarvest/OnCityAIPrepare),经位置保持交换替换内核 handler——不改内核代码,溶解缝显式登记于 SangoLegacyBridge |
| 对拍 digest 双源 | 2 | `SangoTurnDriver.WorldDigest` 城域行(金/粮/人口/耐久/归属/名单指纹)经 SangoCityNativeRuntime 挂载态选择源(组件源=正式源,未挂载=内核源) |

### 3. Reuse list

- Handlers: 不动任何 GAS handler;复用 `AttributeMutationOps.SetBase`、`EntityLifecycleAtomicOps.MaterializeTemplate`。
- Queues / Systems: 不新增队列;引擎系统注册进既有 `SystemGroup.Cleanup`(与 SangoEntityMirrorSystem 同相位)与 `SystemGroup.InputCollection`(UtilityAiThinkScheduleSystem 的 AI 相位)。
- Resolvers / Registries: `IModContext.Registries.RegisterAttribute`(引擎 AttributeRegistry 唯一注册点)、`GameEngine.MapLoader.TemplateRegistry`/`EntityTemplateKeys`、`CoreServiceKeys.TagOps/PresentationStableIdAllocator/PresenterEntityRuntime/PresenterDefinitionRegistry`。
- Existing presets / graphs: 不新增、不修改。内核公式移植源:`ClassicsCityWorking.OnCityCalculateHarvest/OnCityMonthStart/OnCitySeasonStart/OnCityAIPrepare`、`BuildingWorking.OnCityCalculateHarvest`(经典模式复合收获链:两订阅者按订阅序先后覆盖 totalGain*,原生实现按同序复合)、`City.OnMonthStart`(俸给 GoldCost)、`City.FoodCost/CostFood`(军粮)、`City.JobTrainTroops/JobRewardPersons/JobRecruitPerson/JobSearching`(四型内政令)。

### 4. New Layer 0 ops (if any)

N/A——无新增 op。模板 `sango.city` = Name + AttributeBuffer(4 属性名经 ResolveAttributeBufferAttributeId 解析)+ DirtyFlags;组件以代码 `world.Add` 施加(同 M3.h 模式)。

### 5. Transaction boundary

无需新增 all-or-nothing rollback:城数值写入逐属性 SetBase(引擎 op 自带脏标/表现位一致性);对拍合同保证正确性——原生收入 draw/公式若有误,write-through 使内核 PONO 偏移 → 全量 digest(run A vs run B)失配 fail-fast;组件==内核逐位断言在测试面硬失败。

### 6. Config SSOT

行为配置落在: 模板数据 `mods/sango/SangoSimMod/assets/Entities/templates.json`(引擎既有 EntityTemplate schema,仅数据行);属性名注册在 `SangoSimModEntry.OnLoad`(代码合同);经济/命令公式为代码合同(移植自内核,语义 SSOT=sango-src)。

是否新增 JSON schema: **NO**。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线(MaterializeTemplate 复用;M3.h 镜像实体与原生城实体在本波共存——镜像模板不动,原生城为新增 `sango.city` 模板)
- [x] 未把 placement 校验塞进 lifecycle op(城摆位=内核格坐标投影,同 M3.h)
- [x] 未添加「说不清的」默认 fallback(事件面交换用位置保持重建,找不到目标 handler 即类型化抛错;名单 InlineArray 容量打满即抛错不截断)

### 8. Next variant test

「下一个 Mod 变体」将修改: **effect 步骤 / graph 连线**(后续波把俸给/军粮改为 effect 链或派生属性、武将域(D-2')复用本波属性注册模式)——不触碰 Core enum。

### 附:本波边界与登记

- 不动 `src/`(引擎)、registry/launcher/gitbook、内核 `Game/` City 域代码(预言机);武将/部队/军团域不动。
- 事件面交换(内核 handler 位置保持替换)与一切跨域对象访问(PONO 武将/势力/军团/城)登记于 `SangoLegacyBridge` 文件头;后续波逐条消亡。
- `artifacts/gas-composition-gate.md` 是本波唯一 artifacts/ 变更。
