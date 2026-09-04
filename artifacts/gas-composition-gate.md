# GAS Composition Gate — D-2' 武将域原生重写(正经用 Ludots 第二波)

> 本波自审按 `skills/governance/ludots-gas-composition-gate/SKILL.md` 产出(正式位置 `artifacts/gas-composition-gate.md`)。
> 上一份内容(D-1' 城域原生重写,2026-09-04)见 git 历史(80bb599cf),此处按当前任务覆盖。

## GAS Composition Gate — Self Review

- **Task / Issue**: Sango 移植 M3 末武将域溶解——sango.person 模板升级(镜像分支退役消灭双实体)、武将数值(统/武/智/政/忠诚)GAS 属性化、状态/归属/任务/履历保序定容组件、武将回合结算系统挂 Cleanup 相位、城域对武将的读/写桥(#1/#2/#3)消亡、digest 武将行双源、对拍验收(同种子同命令流逐位相等)
- **Date**: 2026-09-04
- **Agent / Author**: D-2'(移植流水线,武将域原生重写)

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**(武将数值走既有 GAS 属性通道:注册 5 个 attribute id + `AttributeMutationOps.SetBase` 写入,零新 effect/preset/graph op/profile schema;武将实体物化沿用 `MaterializeTemplate` Layer 0 op,模板仅加数据行)

结论: **PASS**(属性通道与实体生命周期全部复用既有正式面,无新 GAS 变体)

一句话理由: 本波零改动 `BuiltinHandlerId`/`EffectPresetType`/graph op/`*_profiles.json`/morph DSL;武将数值 = 既有 AttributeRegistry(Register)+ AttributeBuffer + AttributeMutationOps.SetBase 正式通道(SetBase 自带同值幂等省略,批量同步天然节流);物化/销毁同 M3.h/D-1' 合同(MaterializeTemplate / 非表现 world.Destroy),不建第二条物化管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 武将实体物化(spawn) | 0(复用) | `EntityLifecycleAtomicOps.MaterializeTemplate(services, Entity.Null, "sango.person", posCm)`;模板 `SangoSimMod/assets/Entities/templates.json` 仅加 `sango.person` 数据行(Name+AttributeBuffer(5 属性)+DirtyFlags);851 容量上限语义=内核 personSet 合同(id 0 空位,实存 850,越界类型化抛错) |
| 武将实体销毁 | 0(同构合同) | 非表现实体分支 `world.Destroy` |
| 武将 GAS 属性(sango.person.command/strength/intelligence/politics/loyalty) | 2(数据面,复用引擎通道) | `AttributeRegistry.Register` 注册(SangoSimModEntry.OnLoad 走 context 面,运行时/测试侧 EnsureRegistered 幂等);值写入 `AttributeMutationOps.SetBase`;读取 `AttributeBuffer.GetCurrent` |
| 状态/归属反向引用/任务态/履历/账本组件 | 2(Mod 数据) | SangoSimMod unmanaged struct 组件(自动进 world.bin 序列化发现链);GAS 承载数值,组件承载状态与定容引用 |
| 武将回合结算(忠诚漂移/登场/俘虏逃逸的组件落账) | 2(Mod 行为) | 引擎系统 `SangoPersonSettlementSystem` 注册 `SystemGroup.Cleanup`(与城域结算同相位);读缝同步(digest 读/俸给读/收获因子读/命令结算读)保证组件源时点权威;内核保留面(见下)镜像对账 |
| 武将写面(训练功勋经验/褒奖忠诚/任务态) | 2(Mod 行为) | `SangoPersonWriteFace`(城域 job 结算体的武将写入段:组件写 + 内核 PONO write-through,语句序逐位保持) |
| 事件面交换 | 0(D-1' 原语复用) | `SangoCityEventSwap` 位置保持原语继续承载 D-1' 已交换的城域经济/AI 面;本波新增内核事件订阅(OnPerson* 六面/OnCityFall/回合与命令漏斗)全部为**追加式**(内核无既有订阅者可替换——见 7 红线扫描后的在案结论),陈旧世界守卫(IsCurrentKernel)照搬通用合同 |

### 3. Reuse list

- Handlers: 不动任何 GAS handler;复用 `AttributeMutationOps.SetBase`(自带同值幂等)、`EntityLifecycleAtomicOps.MaterializeTemplate`。
- Queues / Systems: 不新增队列;`SangoPersonSettlementSystem` 注册进既有 `SystemGroup.Cleanup`;事件订阅复用内核既有 GameEvent 面(OnPersonCaptured/OnPersonRelease/OnPersonExecute/OnPersonEscape/OnPersonChangeBelongCity/OnPersonChangCurrentCity/OnCityFall/OnTurnStart/OnTurnEnd/OnDayUpdate)与 `SangoCommandJournal.CommandEffectsApplied` 命令漏斗。
- Resolvers / Registries: `AttributeRegistry`(引擎唯一注册点)、`GameEngine.MapLoader.TemplateRegistry`/`EntityTemplateKeys`、`CoreServiceKeys.TagOps/PresentationStableIdAllocator/PresenterEntityRuntime/PresenterDefinitionRegistry`。
- Existing presets / graphs: 不新增、不修改。武将域语义移植源:`Person.GainExp`(升级链)、`City.JobTrainTroops/JobRewardPersons/JobRecruitPerson` 武将写入段、`City.GoldCost`(俸给)、`BuildingWorking.GetCityLeaderInfuse/GetPersonInfuse`(收获因子)、`Force.OnSeasonStart`/`ForcePersonLoyaltyChange`(换季掉忠)、`Person.OnTurnStart`(登场)、`City/Troop.OnForceTurnEnd`(俘虏逃逸)。

### 4. New Layer 0 ops (if any)

N/A——无新增 op。模板 `sango.person` = Name + AttributeBuffer(5 属性名)+ DirtyFlags;组件以代码 `world.Add` 施加(同 D-1' 模式)。

### 5. Transaction boundary

无需新增 all-or-nothing rollback:武将写入逐字段组件写 + 内核 write-through(语句序保持内核原序,ActionOver 经内核属性 setter 保 OnPersonActionOver 事件位);对拍合同保证正确性——原生写面/读面若有误,write-through 使内核 PONO 偏移或组件源 digest 行失配 → 全量 digest(run A vs run B)fail-fast;组件==内核逐位断言在测试面硬失败(武将行双源逐回合对账 + 俸给/经验/忠诚公式独立复算)。

### 6. Config SSOT

行为配置落在: 模板数据 `mods/sango/SangoSimMod/assets/Entities/templates.json`(引擎既有 EntityTemplate schema,仅数据行);属性名注册在 `SangoSimModEntry.OnLoad`(代码合同);武将域公式为代码合同(移植自内核,语义 SSOT=sango-src)。

是否新增 JSON schema: **NO**。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线(MaterializeTemplate 复用;镜像 sango.mirror.person 分支在原生武将运行时挂载时退役——城域先例,消灭双实体)
- [x] 未把 placement 校验塞进 lifecycle op(武将摆位=城/部队格坐标投影,同镜像先例)
- [x] 未添加「说不清的」默认 fallback(851 容量越界类型化抛错;事件订阅守卫陈旧世界即 no-op,不代位发明数据)

在案结论(事件面交换范围): 内核武将回合结算体(忠诚漂移 `Force.OnSeasonStart`、登场 `Person.OnTurnStart`、俘虏逃逸 `City/Troop.OnForceTurnEnd`)是虚方法链(`Scenario.eventReciveList` 逐对象分发),**无静态事件订阅面可位置保持替换**——全仓核验:GameEvent.OnPerson*/OnForcePersonLoyaltyChangeProbability 的内核订阅者仅 PlayerMessage(UI 消息面)与 ForcePersonLoyaltyChange 修改器(概率覆盖,非结算体)。故本波交换面=D-1' 已交换的城域经济/AI 面(继续有效,代位转发合同不变),武将写面接管沿用 D-1' 命令路由交换(SangoCityOps.ExecuteJob → 原生结算体);武将回合结算按 D-1' 内核保留面先例镜像对账 + 读缝同步,登记于 SangoLegacyBridge 待终局波。

### 8. Next variant test

「下一个 Mod 变体」将修改: **effect 步骤 / graph 连线**(D-3' 部队/军团域复用本波组件/属性注册模式;后续波把换季掉忠/俸给改为 effect 链或派生属性时重过本闸门)——不触碰 Core enum。

### 附:本波边界与登记

- 不动 `src/`(引擎)、registry/launcher/gitbook、内核 `Game/`(预言机);部队/军团/外交域不动。
- 消亡桥:#1(武将 loyalty/state/归属/IsFree 读)、#2(Politics/Command/BaseTrainTroopAbility/Official.cost 读)、#3(merit/GainExp/ActionOver/loyalty/SetMission 写)→ 全部组件化(读写面经 SangoPersonReadFace/SangoPersonWriteFace);新增登记(武将 PONO 引用仅作 id 键、回合结算虚方法保留面、演出事件次回合结算面、GainExp/SetMission 副作用链 write-through)见 SangoLegacyBridge 文件头表。
- `artifacts/gas-composition-gate.md` 是本波唯一 artifacts/ 变更。
