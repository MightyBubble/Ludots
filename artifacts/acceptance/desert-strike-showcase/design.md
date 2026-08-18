# Desert Strike（沙漠风暴）Tug of War — 设计与架构方案

## 1. 玩法定义（对标星际争霸 Desert Strike / 沙漠风暴）

- 1v1：本地玩家（Player 1）对 AI（Player 2），双方各有一座主基地（Command Center，不可移动）。
- 每 30 秒一波：把玩家在此期间购买的部队编成波次，从本方三条出兵线（上/中/下）自动出生。
- 部队自动沿路向敌方基地推进；射程内出现敌对目标即停下交战；敌人被清空后继续推进，直至兵临敌方基地。
- 经济：开局 600 水晶，每 10 秒 +120；击杀不给钱（Desert Strike 经典模型是收入制）。
- 购买：选中本方基地，点击指令面板购买 4 种单位（Marine/Firebat/Goliath/Siege Tank），立即扣费并进入下一波队列；钱不够则拒绝。
- 胜负：摧毁敌方主基地即获胜；基地生命归零即失败。

## 2. 复用清单（发现阶段结论，全部经源码验证）

| 基建 | 复用点 |
|------|--------|
| Mod 脚手架 | `mods/showcases/rts_training_sc2` 同款 csproj/mod.json/entry（`Ludots.Core` 引用） |
| 地图管线 | `assets/Maps/desert_strike.json`：Entities(Template/InstanceId/Overrides) + Teams + Players + ParticipantRelationships（参考 `rts_demo/assets/Maps/rts_entry.json`、`utility_autocast` 地图） |
| 模板管线 | `assets/Entities/templates.json` 组件注入（Name/Team/WorldPositionCm/AttributeBuffer/AbilityStateBuffer/OrderBuffer/blackboards/自定义组件） |
| 敌我判定 | `TeamManager.GetRelationship`（RtsDemoMod 在 GameStart 固化 1↔2 Hostile；本 Mod 同样声明，幂等） |
| 移动 | `MoveToWorldCmOrderSystem`：`AttributeBuffer.base.MoveSpeed` 是唯一移动真相源；无 MoveSpeed = 不可移动建筑 |
| 订单 | `OrderQueue.TryEnqueue`（castAbility 100 / moveTo 101），`Order.Args.Spatial` 单点 WorldCm |
| 伤害 | GAS `InstantDamage` effect（`participatesInResponse`），攻击 = 单位 Attack 技能 castAbility（utility_autocast 同款链路） |
| 技能 | 能力 exec 时间轴：`TagClip`（攻击 GCD）+ `EffectSignal`（伤害/购买 tag）+ `TagSignal`（购买指令） |
| 运行时生成 | `RuntimeEntitySpawnQueue`（Kind=Template + WorldPositionCm + TeamIdOverride + PlayerOwnerIdOverride + receipt 通道），由 `RuntimeEntitySpawnSystem`（EffectProcessing）执行 |
| 属性变更 | `AttributeMutationOps.SetCurrent`（扣费/收入/改血） |
| 标签 | `TagOps.HasTag(TagSense.Effective)` / `RemoveTag`；技能 loader 自动注册引用的 tag（无需 tag_rules.json） |
| UI | 信息 HUD：`PanelTemplate`（schema v1）+ `PanelActivationApi`/`UiPanelActivationStore` + `PanelProjectionReader` + `UiSurfaceHost` ReactivePage 表面（正典面板线，SSOT `gitbook/architecture/ui-panel-template-instance-router.md`）；商店：`IEntityCommandPanelService` 单张 CommandDeck（`gas.ability-slots`，地图 tag `suppress_rts_demo_panels` 关闭 RtsDemo 演示面板） |
| 表现 | RtsDemoMod `rts_actor_ring` presenter（EntitySpawned key="*" 全实体通用）——零 presenter 工作 |
| 验收 | `RtsTrainingShowcaseAcceptanceTests` 骨架（NUnit + `RepoModPaths.ResolveExplicit` + `InitializeWithConfigPipeline` + `LoadMap` + `Tick/TickUntil`） |

## 3. 新增清单（全部落在本 Mod，不动 Core）

| 组件 | 说明 |
|------|------|
| `DesertStrikeUnit` / `DesertStrikeBase` | 空标记组件（经 `ComponentRegistry.Register` 模板注入），界定自动交战/死亡/胜负范围 |
| 8 个 GAS 能力 | 4 个攻击能力（按单位类型不同 GCD/伤害效果）+ 4 个购买能力（TagSignal） |
| 8 个 GAS 效果 | 4 个伤害效果（InstantDamage）+ 4 个购买 tag 无需效果（TagSignal 直接写在能力 exec）——实为 4 个 |
| 9 个 Mod 系统 | Wave（波次）/ AutoBattle（索敌+推进）/ Purchase（购买扣费入队）/ Income（收入）/ Death（死亡与胜负）/ AiPlayer（AI 购买）/ Hud（面板表面适配）/ ShopPanel（商店指令面板）——另有 `DesertStrikeHudPanelRuntime` 面板运行时 |
| `DesertStrikeConfig` | `assets/Configs/desert_strike_config.json`（波次/收入/单位价格/AI 权重），VFS 直读（MobaConfig 同款） |
| 验收测试 | `src/Tests/GasTests/Production/DesertStrikeShowcaseAcceptanceTests.cs` |

不新增任何 Core 枚举、preset、handler、schema——新玩法全部由现有 op 组合 + Mod 系统实现。

## 4. 关键设计决策

1. **自动交战 = 自研 `DesertStrikeAutoBattleSystem`（PostMovement），不用 Utility AI**：
   - Utility AI 的 DecisionSystem 在 OrderBuffer 忙时整帧跳过决策，无法实现"行进中遇敌即战"；
   - `CombatStanceBehaviorMod` 的 attackMove 提交 attackTarget 订单，但 Core 与各 Mod 均无 attackTarget 执行者（侦察结论）；
   - 本系统以 `CombatStanceOrderSystem` 为蓝本：每帧对空闲/行进中单位做 `ISpatialQueryService.QueryRadius` 索敌（TeamManager 敌我判定），射程内提交 castAbility 攻击（castAbility 按 orderRules 打断 moveTo）；GCD 期间静止（TagClip 标签判断，杜绝订单洪泛）；无目标且未到敌方基地时提交 moveTo 推进。
2. **购买走技能 + TagSignal + Mod 消费**（复用 `RtsRelationRuntimeSystem` 的 Command tag 消费模式）：基地技能面板 = 商店；`DesertStrikePurchaseSystem` 校验余额（不足拒绝、绝不透支）、扣费、入下一波队列。
3. **波次用 `RuntimeEntitySpawnQueue`**（引擎官方运行时生成通道，CreateUnit handler 同源后端），出生点 = 本方兵线 marker 位置 + 确定性散开。
4. **死亡 = Mod 轮询**（Core 无死亡系统，narrative showcase 同款模式）：Health<=0 → `CommandBuffer.Destroy`；基地死亡 → 判定胜负。
5. **胜负载体**：`DesertStrikeState.GameOver/WinnerPlayerId`（GlobalContext）+ HUD 横幅 + 日志；停止波次/收入/AI 购买。

## 5. 地图布局（世界坐标 cm，Y 轴为上）

- 基地：P1 (-12000, 0)，P2 (+12000, 0)；基地 HP 6000，无 MoveSpeed。
- 出兵点：配置驱动（`desert_strike_config.json → lanes`），每方 3 路：P1 (-9000, +3500 / 0 / -3500)，P2 (+9000, +3500 / 0 / -3500)，波次出生 = 出兵点 + 确定性散开（不在地图中放 marker 实体，避免多余的 actor 光圈）。
- 双方互斥敌对（ParticipantRelationships Teams 1↔2 Hostile）。
- 波次单位出生后自动向敌方基地推进（`DesertStrikeAutoBattleSystem` 目标 = 敌方基地实时位置）。
- 地图加载后本地玩家指令源自动选中 P1 基地（复用 EntityCollection 指令源 API），指令面板即商店。

## 6. 平衡初值（`desert_strike_config.json` + 模板属性）

| 单位 | 价格 | HP | 速度 cm/s | 射程 cm | 伤害 | GCD ticks |
|------|------|----|-----------|---------|------|-----------|
| Marine | 75 | 180 | 620 | 300 | 10 | 30 |
| Firebat | 150 | 260 | 560 | 220 | 18 | 40 |
| Goliath | 300 | 420 | 500 | 560 | 26 | 60 |
| Siege Tank | 450 | 520 | 340 | 920 | 48 | 120 |

- 开局 600 水晶；收入每 600 ticks（10s）+120；波次每 1800 ticks（30s）。
- 首波 = 双方各 6 Marine（每路 2）starter wave；后续波次 = 期间购买队列。
- AI：每 120 ticks 思考一次，按权重 {Marine 4, Firebat 2, Goliath 2, Tank 1} 购买可负担单位。

## 7. 验收标准

1. 地图加载后两基地 + 六 marker 存在。
2. 购买：扣费正确、钱不够拒绝、下一波实际出生对应单位。
3. 自动交战：双方部队接触后发生伤害/死亡；单位朝敌方基地方向位移。
4. 胜负：基地 Health 归零 → 实体销毁 + `GameOver` + 胜者正确。
5. 产物：`artifacts/acceptance/desert-strike-showcase/{trace.jsonl,battle-report.md}`。
