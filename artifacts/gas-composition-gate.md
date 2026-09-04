# GAS Composition Gate — D-4' 战斗解算 GAS 化(正经用 Ludots 第四波)

> 本波自审按 `skills/governance/ludots-gas-composition-gate/SKILL.md` 产出(正式位置 `artifacts/gas-composition-gate.md`)。
> 上一份内容(D-3' 部队/军团域,2026-09-04)见 git 历史(3d41d7aac),此处按当前任务覆盖。

## GAS Composition Gate — Self Review

- **Task / Issue**: Sango 移植 M3 末战斗解算溶解——SkillInstance.Action 解算本体(普攻/技能伤害、反击、击退撞击、士气变化、兵力增减、俘获掷点、攻城守军/耐久两段、技能效果链、能量扣减)迁到 Ludots GAS 正式面:28 技能(Skills.json)映射 ability/effect 模板(数据驱动 JSON 入 mod assets),解算语义落成 mod builtin 阶段 handler + mod preset type(无新 Core enum),调用面经引擎正式激活面(AbilitySystem.TryActivateAbility → EffectRequestQueue → EffectProcessingLoopSystem 同步排空),内核解算经演出事件转移(divert)停走(预言机路径保留,未挂载时照旧);桥 #4b/#6 战斗面移交;对拍 = 同种子同命令流内核源 vs 原生源全量 digest 逐位相等
- **Date**: 2026-09-04
- **Agent / Author**: D-4'(移植流水线,战斗解算 GAS 化)

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**(新 effect 步骤(mod builtin 阶段 handler key `SangoSimMod.SangoSkillAction`,经 `IModContext.Extensions.Gas.RegisterBuiltinHandler` 文档面注册)+ 28 个 effect 模板 `Effect.Sango.Skill.{id}`(configParams 携带技能数值,Skills.json SSOT 派生)+ mod preset type `SangoSkillAction`(preset_types.json 指向 handler key——gas-layered-architecture §4 的 Mod 扩展正式写法);零新 Core `EffectPresetType`/`BuiltinHandlerId` enum、零新 profile schema、零 morph/lifecycle 改动)

结论: **PASS**(变体 = effect 模板数据行 + 参数;下一个技能 = 新模板行,不改代码不改编译分支)

一句话理由: 技能间差异全部是数据位(atk/atkDurability/costEnergy/atkOffsetPoint/offsetAction/blockFactor/canDamage* 标志),内核解算序(SkillInstance.Action)对全部技能不变——28 技能 = 28 模板行引用同一 preset;变体表达力落在 effect template(configParams)与既有 GAS 执行面(激活→请求→阶段执行→属性写)上,不触碰任何 Core enum 或 loader 分支。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 技能→GAS 内容(28 技能参数) | 2(内容 SSOT) | `SangoSimMod/assets/GAS/effects.json`:28 行 `Effect.Sango.Skill.{id}`(Instant,presetType=SangoSkillAction,configParams=Skills.json 行的数值位;测试断言映射与 Skills.json 逐字段相等——SSOT 派生不硬编码) |
| 技能→ability 面 | 2(内容) | `SangoSimMod/assets/GAS/abilities.json`:28 行 `Ability.Sango.Skill.{id}`(onActivate→对应模板);运行时把 caster 部队实体的当前施放位(AbilityStateBuffer 单槽)指向被施放技能的 ability id,激活走 `AbilitySystem.TryActivateAbility` 全验证链 |
| 解算语义(伤害公式/反击/击退/俘获/攻城两段/能量扣减/技能效果链) | 2(Mod 行为) | mod builtin 阶段 handler `SangoSimMod.SangoSkillAction`(BuiltinHandlerFn 签名,经 BuiltinHandlerRuntimeScope 拿运行时服务);内部按内核 SkillInstance.Action 语句序组织为有序步骤段(每段=内核一段语义,公式逐位转写);内核事件群(OnSkillDamageTroop/OnTroopChangeTroops/OnTroopChangeMorale/OnTroopDestroyed/OnSkillDamageTroopAfter/OnSkillDamageBuildingTroops/OnSkillDamageBuildingDurability/OnTroopCalculateAttackBack/OnBuildCalculateAttackBack/OnSkillActionOver/OnSkillActionEnd)在与内核相同位置触发(战报订阅面不变) |
| preset type | 3(Mod 糖衣) | `SangoSimMod/assets/GAS/preset_types.json`:`SangoSkillAction` → OnApply 默认 handler = builtin key `SangoSimMod.SangoSkillAction`(文档面:preset 可指向注册 handler key);不进 Core enum |
| 数值写入 | 0(复用) | 部队兵力/士气/携粮 = `AttributeMutationOps.SetBase`(D-3' 既有属性通道) + 内核 PONO write-through(内核 AI/读面继续消费内核值);城耐久 = sango.city.durability 既有属性 + 城域运行时边界同步;城兵力 = PONO write-through(城属性无 troops 位,D-1' 合同) |
| 激活/执行 | 0(复用引擎类) | `AbilitySystem.TryActivateAbility`(引擎正式激活面)→ `EffectRequestQueue.Publish` → `EffectProcessingLoopSystem.Update` 同步排空(GasTests 既有 headless 同步范式;战斗解算必须在内核施放位同步完成——随机流次序铁律);运行时自持 GAS 栈(引擎宿主与 headless 同构:同一份资产 JSON 经引擎 ConfigPipeline/loader 类编译,不建平行加载器) |
| 战斗路径改道(内核解算停走) | 2(Mod 路由) | `SangoCombatNativeRuntime` 演出队列转移:三个 mod 持泵位(SangoTurnDriver.AdvanceTurn 每 Run() 前、SangoTroopOps.PumpRenderEvents、SangoPlayerTurnOps 泵位)以逐事件 FIFO 泵复刻 RenderEvent.Update 循环形状(依赖队列先行、头部未完即止),TroopSpellSkill{,Critical,Fail}Event 三型在 Enter(内核演出副作用原样)后改走 GAS 激活,OnSkillActionEnd 与 IsDone 完成语义原位补齐,Troop.SpellSkill 守卫位由内核自行完结;反射仅读内核私有队列字段(漂移即类型化抛错,SangoCityEventSwap 同款合同)。未挂载 = 照旧内核泵(预言机路径,零改动) |
| 保留面(内核继续执行,不转写) | P2/P4 同款 | CheckSuccess/CheckCritical(释放判定,非本波移交面——内核 SpellSkill 原位掷点,共享随机流);Troop.UpdateCell(位移,无掷点);City.OnFall(城陷链,城域保留面,内核掷点原位);Troop.Clear(解散共用路径);SkillEffect.Action(AddBuff/SetFire 等技能效果,内核掷点原位——经反射读 SkillInstance.protected effects 列表);DuelManager(单挑解算内核既有) |
| Challenge 调用面 | 2(Mod 行为) | SangoChallengeOps 触发订阅面不变(OnSkillDamageTroopAfter);舌战结果写入(±10 士气)改走 GAS 激活(`Ability.Sango.Challenge.Debate`→morale 步骤);单挑内核回写(HandleDuelResult)为内核保留面 |
| 持久化 | 0(复用) | Instant 效果无持久态;权威数值落 GAS 属性(AttributeBuffer 随 world.bin,D-1'/D-3' 已验);内核战斗捕获面(SaveParticipant 的 SkillInstance/演出态面)标退役候选(SangoLegacyBridge 登记) |

### 3. Reuse list

- Handlers: 复用 `BuiltinHandlers.RegisterAll` 全套(自持栈内置注册);新增仅 mod key `SangoSimMod.SangoSkillAction`(经 Extensions.Gas 文档面;引擎宿主 OnLoad 注册入引擎 hub 使引擎管线可编译本 mod 模板,headless 由运行时注册入自持 registry——同一函数)。
- Queues / Systems: `EffectRequestQueue`、`AbilitySystem`、`EffectProcessingLoopSystem`(GasTests 同构构造)、`GasClocks`/`DiscreteClock`;内核事件面 GameEvent 全群(追加式,陈旧世界守卫照搬通用合同);`SangoCommandJournal.CommandEffectsApplied`。
- Resolvers / Registries: `AttributeRegistry`(既有 sango.troop.*/sango.city.durability)、`EffectTemplateRegistry`/`EffectTemplateIdRegistry`/`PresetTypeRegistry`/`AbilityDefinitionRegistry`(引擎 loader 编译本 mod 资产)、`ConfigPipeline`+`ModLoader`(引擎配置基建,无平行加载器)、D-3' `SangoTroopNativeRuntime`(部队实体/组件/属性)、D-1' `SangoCityNativeRuntime`(城实体)。
- Existing presets / graphs: 不修改任何 Core preset/graph;`AttributeMutationOps.SetBase`、`EntityLifecycleAtomicOps`(既有,本波不新增物化)。
- 内核移植源(转写基准): `SkillInstance.Action/CheckSuccess/CheckCritical`、`Troop.CalculateSkillDamage×5/CalculateRestrainBoost/ChangeTroops/ChangeMorale/OnDestroy/GainEP/GainTargetResource/GetAttackBackFactor`、`City.ChangeTroops/OnFall`、`BuildingBase.ChangeDurability/GetAttackBackFactor/GetAttackBack/GetSkillMethodAvaliabledTroops`、`Skill.GetAttackCells`、`SkillInstance.DoOffset/DoEffect`、`TroopSpellSkill{,Critical,Fail}Event`。

### 4. New Layer 0 ops (if any)

N/A——无新 Core op。唯一新 handler 是 mod builtin key(Layer 2 Mod 行为,文档面注册);物化/属性/派发全部复用 Layer 0 既有。

### 5. Transaction boundary

解算整体在一个同步施放位内完成(激活→请求→阶段执行→属性写→内核 write-through,EffectProcessingLoopSystem 排空后返回);引擎 Effect 事务(EffectPhaseSideEffectTransaction)对本模板按 GasTransactional 元数据走既有流程。跨步骤 all-or-nothing 的正确性由对拍合同兜底:任何一步写错一位即 digest 失配 fail-fast(内核源 vs 原生源全量逐位 + 组件==内核断言 + 公式手算复算)。

### 6. Config SSOT

行为配置落在: `SangoSimMod/assets/GAS/effects.json` + `abilities.json` + `preset_types.json`(引擎既有 GAS 资产 schema;参数源 SSOT = `SangoContentMod/assets/Data/Common/Skills.json`,测试断言派生相等);解算公式为代码合同(语义 SSOT = sango-src/内核预言机)。

是否新增 JSON schema: **NO**(全部落在引擎既有 effects/abilities/preset_types schema)。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum(变体 = 模板数据行)
- [x] 未新建与 spawn 平行的物化管线(部队实体沿用 D-3' MaterializeTemplate 产物,仅加 AbilityStateBuffer 单槽组件)
- [x] 未把 placement 校验塞进 lifecycle op(施放格判定在内核释放链,不在 GAS 面)
- [x] 未添加「说不清的」默认 fallback(未挂载 = 内核路径照旧,显式双态;反射接缝漂移即抛错;GAS 栈缺服务即类型化失败)

在案结论(战斗路径路由): 内核解算体(SkillInstance.Action)由演出事件(TroopSpellSkill*Event.Update)直调,无静态事件订阅面可位置保持替换(全仓核验:三事件型的调用方只有 RenderEvent 队列泵);泵位三处中两处是 mod 代码、一处(Scenario.Run)在内核但被 SangoTurnDriver 逐帧前馈驱动——故交换面 = mod 持泵位的逐事件转移(divert),反射只读私有队列字段(漂移抛错),内核代码零改动、未挂载即预言机路径。桥 #4b(部队数值读)与 #6 战斗面(城 PONO 读)随解算转写消亡;#6 城域其余面按 D-1' 计划留城域终局。

### 8. Next variant test

「下一个 Mod 变体」将修改: **effect 模板数据行**(新技能 = Skills.json 新行 → 新 Effect.Sango.Skill.{id} 模板行,引用同一 preset/handler)——不触碰 Core enum、不改编译分支。

### 附:本波边界与登记

- 不动 `src/`(引擎)、registry/launcher/gitbook、内核 `Game/`(预言机,零改动)。
- **落地补记(实现期发现,与 §2 的差异)**:
  - 同步执行栈显式配线:`EffectProcessingLoopSystem` 需要显式 `EffectPhaseExecutor`(programs/presetTypes/builtinHandlers/graphHandlers/templates 五元组)与 `GasGraphRuntimeApi`(world+requests+tagOps),缺省即拒——自持栈按 GasTests 同构配线,handler 经我方 BuiltinHandlerRegistry(引擎 builtin + `SangoSimMod.*` 两键)解析。
  - **引擎缺口登记(abilities.json 同步表达力)**:abilities.json 的 exec 时间线由帧驱动的 AbilityExecSystem 承载,`onActivateEffects` 字段被 loader 显式禁止——JSON 层无法表达"激活即同步发布"。运行时按引擎语义等价派生:exec 的 tick-0 EffectSignal → AbilityDefinition.OnActivateEffects(`DeriveSynchronousOnActivateEffects`,数据源仍是 abilities.json,非新 schema)。引擎侧若提供正式的同步激活写法,该派生退位。
  - 自持栈的 ConfigPipeline 需要 LudotsCoreMod 的 game.json 常量(responseChainOrderTypeIds)——mod 根解析合同:引擎 VFS 解析位 `<root>/assets/*` 需剥 assets 层(AttachBare 传仓内根)。
- **运行时证据(引擎宿主,raylib + AgentBridge,本波实测)**:sango_default 图 + SangoSeedBattle/SangoStepTurns 共 ~30 回合世界战争;`entities.query` 部队实体 33 支;`gas.entity` 部队 GAS 属性战斗磨损(兵力 960/1082/2060/2565,士气 17-100——即 D-4' 解算链 SetBase 写入面);`gas.diagnostics` 零容量告警;logs.tail 零 Error(修复前曾暴露 hosted mod 根解析错位——已修)。
- 撞墙/引擎缺口预登记: (a) AbilityStateBuffer 容量 8 < 部队技能上限 24——用"当前施放位"单槽运行时管理(激活仍走 TryActivateAbility 全验证链),若引擎后续需要常驻全技能槽位,登记引擎侧扩容评估;(b) EffectProcessingLoopSystem 为自持同步栈(引擎宿主内与引擎主管线并存,战斗请求不进引擎帧循环)——**其私有加载会 Clear 全局 EffectTemplateIdRegistry/AbilityIdRegistry**(引擎已编译完成后实例注册表不受影响,但后续按名查询全局表会失配)——单世界同步解算的正式形态(引擎服务键暴露 PresetTypeRegistry/BuiltinHandlerRegistry 或 mod 栈注册表命名空间隔离)移交引擎侧评估;(c) 击退位移+撞击伤害的原子性:内核语义本身是"逐格位移、撞停即伤"(顺序非原子),现有表达力足够,未触发缺口上报;(d) 内核解算体无事件订阅面可位置保持替换(直调链),交换面=mod 持泵位逐事件转移(反射只读内核私有队列字段,漂移即抛错)——D-5' 删内核时该泵随之退场。
- 消亡桥:#4b(战斗面部队数值读)+ #6 战斗面(城 troops/durability 战斗写)→ GAS 解算正式面;#4c(战斗解算调用链/SkillInstance/演出事件/PONO 存档捕获面)登记退役候选,消亡波 D-5'。
- `artifacts/gas-composition-gate.md` 是本波唯一 artifacts/ 计划变更(运行时取证经 AgentBridge 会话,截图未产生——战斗证据取 gas.entity/entities.query/gas.diagnostics 数据面;启动日志落 tmp/d4p_launch*.log)。
