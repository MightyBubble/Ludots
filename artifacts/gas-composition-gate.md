# GAS Composition Gate — D-3' 部队/军团域原生溶解(正经用 Ludots 第三波)

> 本波自审按 `skills/governance/ludots-gas-composition-gate/SKILL.md` 产出(正式位置 `artifacts/gas-composition-gate.md`)。
> 上一份内容(D-2' 武将域,2026-09-04)见 git 历史(e8cc0a490),此处按当前任务覆盖。

## GAS Composition Gate — Self Review

- **Task / Issue**: Sango 移植 M3 末部队/军团域溶解——sango.troop 镜像模板升级为原生模板(镜像部队分支退役)、sango.corps 新实体、部队数值(兵力/士气/粮)GAS 属性化、任务态/技能 CD/移动路径态/编成/位置显式可序列化组件(根治 TroopMissionBehaviour 懒重建的组件面)、军团 AP/jobCounter 读写面化(消 SangoLegacyBridge 桥 #4)、部队/军团回合结算系统挂 Cleanup 相位、digest 部队行双源、对拍验收(同种子同命令流逐位相等)
- **Date**: 2026-09-04
- **Agent / Author**: D-3'(移植流水线,部队/军团域原生溶解)

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**(部队/军团数值走既有 GAS 属性通道:注册 3 个部队 attribute id + `AttributeMutationOps.SetBase` 写入,零新 effect/preset/graph op/profile schema;实体物化沿用 `MaterializeTemplate` Layer 0 op,模板仅加数据行)

结论: **PASS**(属性通道与实体生命周期全部复用既有正式面,无新 GAS 变体)

一句话理由: 本波零改动 `BuiltinHandlerId`/`EffectPresetType`/graph op/`*_profiles.json`/morph DSL;部队数值 = 既有 AttributeRegistry(Register)+ AttributeBuffer + AttributeMutationOps.SetBase 正式通道;物化/销毁同 M3.h/D-1'/D-2' 合同(MaterializeTemplate / 非表现 world.Destroy),不建第二条物化管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 部队实体物化(生) | 0(复用) | `EntityLifecycleAtomicOps.MaterializeTemplate(services, Entity.Null, "sango.troop", posCm)`;模板 `SangoSimMod/assets/Entities/templates.json` 加 `sango.troop` 数据行(Name+AttributeBuffer(3 属性)+DirtyFlags);生灭事件驱动(OnTroopCreated/OnTroopClear/OnTroopDestroyed,内核既有订阅面) |
| 部队实体销毁(灭) | 0(同构合同) | 非表现实体分支 `world.Destroy`(OnTroopClear=解散共用路径/OnTroopDestroyed=溃灭,两事件都到即幂等) |
| 军团实体物化/销毁 | 0(复用) | 同 op;模板 `sango.corps` 数据行(军团数=剧本势力军团集,静态于剧本装载,OnCorpsCreate/OnCorpsDelete 事件即时化增删) |
| 部队 GAS 属性(sango.troop.troops/morale/food) | 2(数据面,复用引擎通道) | `AttributeRegistry.Register`(SangoSimModEntry.OnLoad 走 context 面,运行时/测试侧 EnsureRegistered 幂等);**值变更**写入 `AttributeMutationOps.SetBase`(同值幂等);**物化期初值**直写 `AttributeBuffer.SetBase`(与模板数据初始化同义,非变更)——运行时启动实测:D-2' 武将挂载批(850×5=4250 首拍变更)超出引擎 AttributeChanged 延迟队列容量(1024,GAS.DEFERRED_TRIGGER.ERR.CapacityExceeded,D-3' 启动实测并在本波修复);读取 `AttributeBuffer.GetCurrent` |
| 部队组件(身份/格位/任务态/技能 CD/移动路径态/编成/俘囚名单/回合账本) | 2(Mod 数据) | SangoSimMod unmanaged struct 组件(InlineArray 保序;自动进 world.bin 序列化发现链) |
| 军团组件(AP/jobCounter 切片/编成成员保序/回合发放账本) | 2(Mod 数据) | 同上;AP 门槛与扣减读写面化(消桥 #4) |
| 部队/军团回合结算(耗粮/断粮士气/出征天数/AP 发放/jobCounter 清零的组件落账) | 2(Mod 行为) | 引擎系统 `SangoTroopSettlementSystem` 注册 `SystemGroup.Cleanup`(与城/武将结算同相位);内核保留面(Troop/Corps.OnForceTurnStart 虚方法链)镜像对账 + 读缝同步(同 D-2' P2 在案结论) |
| 部队写面(任务态/移动路径态) | 2(Mod 行为) | `SangoTroopWriteFace`(组件写 + 内核 write-through:kernel SetMission/missionParams 赋值原样触发,语句序保持) |
| 军团读写面(消桥 #4) | 2(Mod 行为) | `SangoCorpsReadFace`/`SangoCorpsWriteFace`(AP 门槛/扣减、军团级 jobCounter;内核 Corps 成员 write-through,IsPlayer 门与 OnCorpsActionPointChange 事件位原样保持) |
| 事件面 | 0(D-1' 原语复用) | 内核部队/军团回合结算体是虚方法链(`Force.OnForceTurnStart` 直调 `c.OnForceTurnStart`),无静态事件订阅面可位置保持替换——全仓核验 OnTroopTurnStart/OnCorpsActionPointChange 等的内核订阅者均为 Action 修改器/Trigger/Buff 效果(玩法面,必须继续生效),无可交换的结算体。故本波订阅全部**追加式**(陈旧世界守卫照搬通用合同);D-1' 已交换的城域经济/AI 面继续由 SangoCityEventSwap 位置保持承载,非权威期代位转发合同不变 |

### 3. Reuse list

- Handlers: 不动任何 GAS handler;复用 `AttributeMutationOps.SetBase`(自带同值幂等)、`EntityLifecycleAtomicOps.MaterializeTemplate`。
- Queues / Systems: 不新增队列;`SangoTroopSettlementSystem` 注册进既有 `SystemGroup.Cleanup`;事件订阅复用内核既有 GameEvent 面(OnTroopCreated/OnTroopEnterCell/OnTroopLeaveCell/OnTroopClear/OnTroopDestroyed/OnTroopChangeTroops/OnTroopChangeMorale/OnTroopTurnStart/OnTroopTurnEnd/OnCorpsCreate/OnCorpsDelete/OnCorpsActionPointChange/OnCityFall/OnTurnStart/OnTurnEnd)与 `SangoCommandJournal.CommandEffectsApplied` 命令漏斗。
- Resolvers / Registries: `AttributeRegistry`(引擎唯一注册点)、`GameEngine.MapLoader.TemplateRegistry`/`EntityTemplateKeys`、`CoreServiceKeys.TagOps/PresentationStableIdAllocator/PresenterEntityRuntime/PresenterDefinitionRegistry`、`PresenterParamKeyRegistry`(部队标记势力色,既有 key)。
- Existing presets / graphs: 不新增、不修改。部队/军团域语义移植源:`Troop.OnForceTurnStart`(出征天数/断粮伤兵士气/耗粮/技能 CD 推进)、`Troop.SetMission/ClearMission/NeedPrepareMission`、`Troop.OnDestroy/EnterCity/Clear`(俘获/溃灭/入城)、`Corps.OnForceTurnStart`(jobCounter 清零/AP 发放 AddActionPoint)、`Corps.ReduceActionPoint/AddJobCounter/GetJobCounter`、`SangoTroopOps.CreateTroop/MoveTroop`(编成/移动/委任链)。

### 4. New Layer 0 ops (if any)

N/A——无新增 op。模板 `sango.troop` = Name + AttributeBuffer(3 属性名)+ DirtyFlags(+presenter owner 迁移所需 VisualTransform/CullState 组件行);`sango.corps` = Name + DirtyFlags;组件以代码 `world.Add` 施加(同 D-1'/D-2' 模式)。

### 5. Transaction boundary

无需新增 all-or-nothing rollback:部队/军团写入逐字段组件写 + 内核 write-through(语句序保持内核原序,SetMission 经内核成员保 NeedPrepareMission 位);对拍合同保证正确性——原生写面/读面若有误,write-through 使内核 PONO 偏移或组件源 digest 行失配 → 全量 digest(run A vs run B)fail-fast;组件==内核逐位断言在测试面硬失败(部队行双源逐回合对账 + AP/耗粮/士气/CD 公式独立复算)。

### 6. Config SSOT

行为配置落在: 模板数据 `mods/sango/SangoSimMod/assets/Entities/templates.json`(引擎既有 EntityTemplate schema,仅数据行);属性名注册在 `SangoSimModEntry.OnLoad`(代码合同);部队/军团域公式为代码合同(语义 SSOT=sango-src)。

是否新增 JSON schema: **NO**。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线(MaterializeTemplate 复用;镜像 sango.mirror.troop 分支在原生部队运行时挂载时退役——城/武将先例,消灭双实体)
- [x] 未把 placement 校验塞进 lifecycle op(部队摆位=格坐标投影,同镜像先例)
- [x] 未添加「说不清的」默认 fallback(事件订阅守卫陈旧世界即 no-op;挂载态实体缺席即类型化抛错,不静默回退;不代位发明数据)

在案结论(事件面交换范围): 内核部队回合结算体(Troop.OnForceTurnStart 耗粮/士气/CD 推进)与军团结算体(Corps.OnForceTurnStart 的 jobCounter.Clear + AddActionPoint)是虚方法链(Scenario.Run → Force.OnForceTurnStart 直调),**无静态事件订阅面可位置保持替换**——全仓核验:OnTroopTurnStart 的内核订阅者是 Trigger/Buff 效果(玩法修改器,必须继续生效),OnCorpsActionPointChange 零内核订阅者(UI 面)。故本波交换面=D-1' 已交换的城域经济/AI 面(继续有效,代位转发合同不变);部队/军团结算按 D-2' 内核保留面先例镜像对账 + 读缝同步,登记于 SangoLegacyBridge 待终局波(D-5')。

### 8. Next variant test

「下一个 Mod 变体」将修改: **effect 步骤 / graph 连线**(D-4' 技能/效果全 GAS 化复用本波组件/属性注册模式,重过本闸门)——不触碰 Core enum。

### 附:本波边界与登记

- 不动 `src/`(引擎)、registry/launcher/gitbook、内核 `Game/`(预言机);势力域(桥 #5)与演出面(桥 #11/P3)不动。
- 撞墙记录(运行时启动):D-2' 武将域挂载批在引擎 AttributeChanged 延迟队列上超容(4250 > 1024)——headless 测试不走引擎 GAS 系统拍故不暴露;本波 raylib 实测命中,mod 侧修复 = 物化期初值直写 AttributeBuffer(非变更路径),值变更仍走 SetBase 正式通道。同修复应用于本波部队物化(保持同波同式)。城域(88×4=352 < 1024)未动。
- 消亡桥:#4(军团 ActionPoint/ReduceActionPoint/GetJobCounter/AddJobCounter)→ 读写面化(SangoCorpsReadFace/SangoCorpsWriteFace;SangoCityOps 门槛/SangoCityJobOps 结算/SangoTroopOps 编成门槛/SangoCityNativeRuntime.SyncCity 全部改走读写面)。战斗解算本体(SkillInstance/ChangeTroops,桥 #6 战斗面)是 D-4' 的面,本波只做承载域,解算调用路径经组件读写面(数值同步挂在回合/势力回合边界与命令漏斗)。
- `artifacts/gas-composition-gate.md` 是本波唯一 artifacts/ 变更(agent-bridge 取证截图属运行时证据,落 artifacts/agent-bridge/shots/)。
