# OpenRA AI 底层系统源码分析

本文是对 OpenRA `bleed` 分支 AI 底层实现的源码级参考分析，面向 Ludots 后续做 RTS / 战略 AI 架构取舍时使用。它不是 Ludots 正式规范，只是外部项目研究材料。

## 1 版本与范围

- 本地源码位置：`external/reference/OpenRA`
- 下载方式：GitHub codeload zip。`git clone` 两次超时后改用源码归档。
- OpenRA 分支：`bleed`
- GitHub API 在下载前报告的 HEAD：`cb4671f6b7b516a30dc49eade03f139cd63a2234`
- 主要分析范围：
  - `OpenRA.Game` 的 Player、Order、World、Trait 基础设施
  - `OpenRA.Mods.Common/Traits/Player/ModularBot.cs`
  - `OpenRA.Mods.Common/Traits/BotModules/**`
  - `mods/ra/rules/ai.yaml`
  - `mods/cnc/rules/ai.yaml`
- 未展开范围：
  - 每一张 campaign 地图的 Lua 脚本 AI。OpenRA 的战役图有大量 `*-AI.lua`，那是地图脚本层，不是通用 skirmish bot 底层。
  - 每个 mod 的所有 AI 配置逐项对比。本文重点看 Common 层和 RA/CnC 两个代表性配置。

## 2 一句话结论

OpenRA 的 skirmish AI 不是一个全局“大脑”，也不是行为树框架。它是挂在 `Player` 系统 actor 上的一组 trait 模块：

```text
Lobby 选择 BotType
-> PlayerActor 上的 IBot trait 被 host 激活
-> ModularBot 每 tick 调用启用的 IBotTick 模块
-> 模块只读世界、做局部启发式决策、排队 Order
-> ModularBot 分批把 Order 交给 World.IssueOrder
-> OrderManager / UnitOrders / Actor.ResolveOrder 进入正常玩家命令链路
-> 游戏状态由通用 order/activity/trait 系统修改
```

最关键的底层边界是：Bot 逻辑不直接改世界状态，只能发 order。OpenRA 甚至把 Bot tick 包在 `Sync.RunUnsynced` 中，明确把 AI 决策视为 host-local 的命令生成器，而不是同步世界模拟的一部分。同步、回放和作弊校验依赖的是 order 流，而不是 AI 内部状态。

## 3 AI 的系统入口

### 3.1 Bot 类型从 PlayerActor trait 暴露

OpenRA 用 `IBotInfo` 暴露可选 Bot 类型：

- 源码：`OpenRA.Game/Traits/TraitsInterfaces.cs`
- 关键接口：
  - `IBotInfo.Type`
  - `IBotInfo.Name`
  - `IBot.Activate(Player p)`
  - `IBot.QueueOrder(Order order)`
  - `IBot.Player`

`ModularBotInfo` 和 `DummyBotInfo` 都实现 `IBotInfo`。`ModularBot` 是通用 skirmish AI，`DummyBot` 是占位 bot，常用于地图脚本自己控制行为的场景。

源码入口：

- `OpenRA.Mods.Common/Traits/Player/ModularBot.cs`
- `OpenRA.Mods.Common/Traits/Player/DummyBot.cs`

### 3.2 大厅只看 PlayerActor 上有哪些 IBotInfo

大厅 UI 和服务端逻辑会从地图的 Player actor 规则里读取 `IBotInfo`：

- `OpenRA.Mods.Common/Widgets/Logic/Lobby/LobbyUtils.cs`
- `OpenRA.Mods.Common/ServerTraits/SkirmishLogic.cs`
- `OpenRA.Mods.Common/Traits/World/CreateMapPlayers.cs`

RA 的配置里直接声明：

```yaml
Player:
    ModularBot@RushAI:
        Name: bot-rush-ai.name
        Type: rush
    ModularBot@NormalAI:
        Name: bot-normal-ai.name
        Type: normal
    ModularBot@TurtleAI:
        Name: bot-turtle-ai.name
        Type: turtle
    ModularBot@NavalAI:
        Name: bot-naval-ai.name
        Type: naval
```

对应文件：`mods/ra/rules/ai.yaml`。

CnC 则声明 `cabal`、`watson`、`hal9001`：

```yaml
Player:
    ModularBot@Cabal:
        Type: cabal
    ModularBot@Watson:
        Type: watson
    ModularBot@HAL9001:
        Type: hal9001
```

对应文件：`mods/cnc/rules/ai.yaml`。

### 3.3 Player 构造阶段只在 host 激活 Bot

`OpenRA.Game/Player.cs` 负责 runtime Player 构造。它会根据 `Session.Client.Bot` 得到 `BotType`，然后：

```text
if IsBot && Game.IsHost:
    在 PlayerActor 上找 Info.Type == BotType 的 IBot
    调用 logic.Activate(this)
```

这意味着 Bot 决策只在 host 侧运行。其他客户端不运行 AI 逻辑，只接收 host 发出的 order 流。这样减少同步复杂度，也避免每个客户端因为 AI 内部启发式差异产生 desync。

## 4 ModularBot 的底层循环

`ModularBot` 是整个 AI 系统的调度器。核心逻辑在 `OpenRA.Mods.Common/Traits/Player/ModularBot.cs`。

它有三类职责：

1. 激活时收集模块：
   - `IBotTick[] tickModules`
   - `IBotRespondToAttack[] attackResponseModules`
   - `IBotEnabled`
2. 每 tick 调用启用模块：
   - 对每个 `IBotTick` 调 `BotTick(this)`
   - 放在 `Sync.RunUnsynced(...)`
3. 把模块排队的 order 分批送入世界：
   - 模块调用 `bot.QueueOrder(order)`
   - `ModularBot` 内部用 `Queue<Order>` 缓存
   - 每 tick 发出最多一部分，受 `MinOrderQuotientPerTick` 控制

它的源码注释写得非常关键：Bot logic 不允许影响 world state，只能 issue orders；这些 orders 会被 replay 记录，所以 replay 中不启用 bots。

这就是 OpenRA AI 最值得学习的系统边界：AI 是“命令生产者”，不是“状态修改者”。

## 5 Order 链路：AI 和玩家走同一套执行系统

### 5.1 订单生成

AI 模块生成的都是正常 `Order`：

- `Order.StartProduction(...)`
- `new Order("PlaceBuilding", ...)`
- `new Order("AttackMove", ...)`
- `new Order("Harvest", ...)`
- `new Order("DeployTransform", ...)`
- `new Order("RepairBuilding", ...)`
- support power 的 order name

这些 order 与玩家 UI 下发的命令同构。

### 5.2 订单派发

链路如下：

```text
IBot.QueueOrder
-> ModularBot.orders
-> World.IssueOrder
-> OrderManager.IssueOrder
-> 网络 / 本地 order buffer
-> UnitOrders.ResolveOrder
-> world.OrderValidators
-> Actor.ResolveOrder
-> IResolveOrder trait
-> Activity / Trait 修改世界状态
```

关键源码：

- `OpenRA.Game/World.cs`
- `OpenRA.Game/Network/OrderManager.cs`
- `OpenRA.Game/Network/UnitOrders.cs`
- `OpenRA.Game/Actor.cs`
- `OpenRA.Mods.Common/Traits/World/ValidateOrder.cs`

### 5.3 订单校验允许 Bot controller 控制 Bot actor

`ValidateOrder` 会检查 order 的 subject owner 是否匹配 client id。Bot 的特殊情况是：

```text
subjectClient.Bot != null
&& clientId == subjectClient.BotControllerClientIndex
```

也就是说，host/admin client 可以作为 Bot controller 为 bot actor 发 order。这保持了安全边界：AI 不能绕过 order validation 去直接改 actor。

## 6 BotModule 组合机制

### 6.1 模块都是 Player actor trait

Common 层的大部分模块都标了：

```csharp
[TraitLocation(SystemActors.Player)]
```

它们不是普通单位身上的 AI component，而是玩家级策略模块。这样它们天然能访问：

- `Player`
- `PlayerActor`
- 所有己方 actor
- 世界资源层
- 生产队列
- 支援技能管理器
- shroud / frozen actor 信息
- pathfinder

### 6.2 用 condition 把 BotType 映射到模块组合

RA 配置先通过 `GrantConditionOnBotOwner` 按 `BotType` 授予 condition：

```yaml
GrantConditionOnBotOwner@rush:
    Condition: enable-rush-ai
    Bots: rush
```

模块再用 `RequiresCondition` 决定是否启用：

```yaml
BaseBuilderBotModule@rush:
    RequiresCondition: enable-rush-ai
```

源码：`OpenRA.Mods.Common/Traits/Conditions/GrantConditionOnBotOwner.cs`。

这样同一套 C# 模块可以组合出不同 AI 性格：

- Rush：更大进攻规模、更激进扩张、更早攻击
- Normal：均衡生产和防守
- Turtle：高防御、攻击间隔极长、地雷模块启用
- Naval：优先海军与空军生产

这不是继承体系，而是配置驱动的模块开关。

### 6.3 模块之间通过小接口协作

Common 层定义了一批 bot 专用接口：

- `IBotTick`
- `IBotEnabled`
- `IBotRespondToAttack`
- `IBotPositionsUpdated`
- `IBotNotifyIdleBaseUnits`
- `IBotRequestUnitProduction`
- `IBotRequestPauseUnitProduction`
- `IBotBaseExpansion`
- `IBotSuggestRefineryProduction`

源码：`OpenRA.Mods.Common/TraitsInterfaces.cs`。

这些接口形成一个很轻的玩家级“模块总线”：

- `SquadManager` 告诉 `UnitBuilder` 当前基地闲置单位数
- `BaseBuilder` 在经济不足时让 `UnitBuilder` 暂停生产单位
- `HarvesterBotModule` 请求 `UnitBuilder` 补造矿车
- `McvExpansionManager` 通知 `BaseBuilder` 新矿区位置，建议造 refinery
- `BaseBuilder` 或 `SquadManager` 被攻击时更新 defense center

优点是模块间没有共同大基类；缺点是隐式耦合比较多，需要读接口调用链才能知道谁影响谁。

## 7 核心模块逐个拆解

### 7.1 ResourceMapBotModule：AI 的资源/威胁粗粒度地图

源码：`OpenRA.Mods.Common/Traits/BotModules/ResourceMapBotModule.cs`

职责：

- 把地图按 stride 切成多个 `ResourceIndice`
- 每个 indice 缓存：
  - 资源格数量
  - 资源中心
  - resource creator 位置
  - 己方 refinery 数
  - 己方 harvester 数
  - 敌方单位数
  - 敌方基地建筑数
  - 友方基地/单位数
- 每隔若干 tick 更新一个 indice，避免一次性扫描全图

它本身不发 order，是给采矿、扩张、基地建造等模块用的空间认知层。

这层非常接近一个低成本 AI blackboard，但 OpenRA 没有把它抽象成通用 blackboard，而是做成具体的 bot module。

### 7.2 BaseBuilderBotModule：基地建造与建筑比例控制

源码：

- `OpenRA.Mods.Common/Traits/BotModules/BaseBuilderBotModule.cs`
- `OpenRA.Mods.Common/Traits/BotModules/BotModuleLogic/BaseBuilderQueueManager.cs`

职责：

- 维护 construction yard、refinery、power、production、defense 等 actor 索引
- 决定下一栋建筑要造什么
- 生产完成后寻找放置位置
- 设置 rally point
- 修正/记录 base center 和 defense center
- 经济不足时暂停单位生产
- refinery 过密或远离资源时卖掉
- 建筑放不下时触发扩张模块

决策优先级大致是：

```text
低电力 -> 造 power
经济不足 -> 造 refinery
现金太多 -> 造 production
需要海军且有水 -> 造 naval production
资源容量满 -> 造 silo
否则按 BuildingFractions / BuildingLimits / BuildingDelays 选建筑
```

位置选择也不是随机乱放：

- 普通建筑围绕 base center 找合法位置
- defense 朝 closest enemy building/attacker 方向靠近
- refinery 优先靠近资源和 expansion request
- naval building 会先检查基地附近是否有水域和 buildable area

这个模块很“老派 RTS”：大量启发式、比例、阈值、延迟和特殊规则，而不是通用 planner。

### 7.3 UnitBuilderBotModule：按目标比例训练单位

源码：`OpenRA.Mods.Common/Traits/BotModules/UnitBuilderBotModule.cs`

职责：

- 周期性寻找空闲生产队列
- 根据 `UnitsToBuild` 的相对比例选择单位
- 遵守 `UnitLimits`
- 遵守 `UnitDelays`
- 响应其他模块的指定单位生产请求
- 被 `IBotRequestPauseUnitProduction` 暂停时不生产

它不是“我要打什么就精确造什么”的 planner，而是维持一个长期军队构成比例。外部请求只处理少数关键补位，例如矿车、MCV。

### 7.4 HarvesterBotModule：采矿维护与矿车自救

源码：`OpenRA.Mods.Common/Traits/BotModules/HarvesterBotModule.cs`

职责：

- 扫描 idle harvester
- 给 idle harvester 下 `Harvest`
- 当找不到资源时增加 cooldown
- 结合 `ResourceMapBotModule` 找低效率矿车并重新分派
- 矿车数低于 refinery 或初始要求时请求 `UnitBuilder` 补矿车
- 矿车被攻击时下 `Dock` 命令回最近矿厂

它在寻路时加了敌人规避成本：

```text
资源目标候选
-> pathfinder 搜索
-> 对敌方威胁 bin 增加额外 cost
-> 找到安全资源格后发 Harvest order
```

这是 OpenRA AI 里少数比较细的局部路径成本策略。

### 7.5 McvManager / McvExpansionManager：基地部署与扩张

源码：

- `OpenRA.Mods.Common/Traits/BotModules/McvManagerBotModule.cs`
- `OpenRA.Mods.Common/Traits/BotModules/McvExpansionManagerBotModule.cs`

`McvManagerBotModule` 比较简单：

- 管理 MCV
- 初始或空闲时部署
- construction yard 数不足时请求建 MCV

`McvExpansionManagerBotModule` 更复杂：

- 依赖 `ResourceMapBotModule`
- 支持 `CheckResource`、`CheckBase`、`CheckCurrentLocation` 三种扩张模式
- 为每个资源 indice 计算 attraction
- 综合考虑：
  - 到当前 MCV 的距离或 path distance
  - 资源量
  - resource creator
  - 敌方基地/单位威胁
  - 己方/友方 construction yard 距离
  - 己方 refinery 距离
  - 其他 active MCV 目标冲突
- 找到中心后，再找可部署 cell
- 失败多次后自动切换扩张模式
- 可把 construction yard undeploy 成 MCV 再搬家

这是 OpenRA 通用 AI 中最接近“战略位置评分”的模块。

### 7.6 SquadManagerBotModule：作战单位组织

源码：

- `OpenRA.Mods.Common/Traits/BotModules/SquadManagerBotModule.cs`
- `OpenRA.Mods.Common/Traits/BotModules/Squads/Squad.cs`
- `OpenRA.Mods.Common/Traits/BotModules/Squads/StateMachine.cs`
- `OpenRA.Mods.Common/Traits/BotModules/Squads/States/*.cs`
- `OpenRA.Mods.Common/Traits/BotModules/Squads/AttackOrFleeFuzzy.cs`

职责：

- 发现新生产出的可作战单位
- 按单位类型分配：
  - air squad
  - naval squad
  - base idle ground units
- 满足 `SquadSize + random bonus` 后组建 Assault squad
- 周期性尝试 Rush
- 被攻击时组建 Protection squad
- 管理 squads 更新和清理

Squad 类型：

- `Assault`
- `Air`
- `Rush`
- `Protection`
- `Naval`

状态机非常轻：

```text
GroundUnitsIdle
-> GroundUnitsAttackMove
-> GroundUnitsAttack
-> GroundUnitsFlee

AirIdle
-> AirAttack
-> AirFlee

ProtectionIdle
-> ProtectionAttack
-> ProtectionFlee
```

攻击或撤退的判断用了 fuzzy logic，但范围很小：`AttackOrFleeFuzzy` 只计算当前己方单位集合 vs 敌方单位集合是否值得打，输入包括：

- 己方血量
- 敌方血量
- 相对攻击力
- 相对速度

它不是全局 fuzzy AI，只是 squad 层局部战斗决策器。

### 7.7 SupportPowerBotModule：支援技能目标选择

源码：

- `OpenRA.Mods.Common/Traits/BotModules/SupportPowerBotModule.cs`
- `OpenRA.Mods.Common/Traits/BotModules/BotModuleLogic/SupportPowerDecision.cs`

职责：

- 遍历已 ready 的 support powers
- 先粗粒度扫描地图 region
- 再在候选 region 内精细扫描 cell
- 用配置化 `Consideration` 计算吸引力
- 没找到目标则延迟下一次扫描
- 找到目标后发 support power order

`SupportPowerDecision` 支持：

- `Against`: Ally / Neutral / Enemy
- `Types`: 目标 target types
- `Attractiveness`: 分值
- `TargetMetric`: Health / Value / None
- `CheckRadius`

RA 的 nuke 配置会给 enemy structures 正分，给 ally units 负分。这个模式很适合做可调的技能目标评分。

### 7.8 BuildingRepairBotModule：建筑维修

源码：`OpenRA.Mods.Common/Traits/BotModules/BuildingRepairBotModule.cs`

职责：

- 被攻击事件触发
- 周期性扫描己方受损 `RepairableBuilding`
- 对单个受损建筑或全部受损建筑发 `RepairBuilding`

它是一个响应型模块，不在每 tick 主动大扫描，借攻击通知做触发入口。

### 7.9 PowerDownBotModule：断电时关停建筑

源码：`OpenRA.Mods.Common/Traits/BotModules/PowerDownBotManager.cs`

职责：

- 监控 `PowerManager.ExcessPower`
- 低电时对指定建筑发 powerdown order
- 电力恢复时逐步重新打开
- 用本地列表追踪被自己关停的建筑

它依赖配置里指定哪些建筑允许 toggle，例如 RA 中 `dome,tsla,mslo,agun,sam`。

### 7.10 CaptureManagerBotModule：占领逻辑

源码：`OpenRA.Mods.Common/Traits/BotModules/CaptureManagerBotModule.cs`

职责：

- 寻找 idle capturer
- 选择敌方或中立的可占领目标
- 按 sell value 排序，限制候选数量
- 检查路径可达
- 发 `CaptureActor`

它体现了 OpenRA AI 的通用套路：每个小能力都做成独立 bot module，用 actor trait 能力判断是否可用。

### 7.11 MinelayerBotModule：地雷特化逻辑

源码：`OpenRA.Mods.Common/Traits/BotModules/BotModuleLogic/MinelayerBotModule.cs`

职责：

- 被攻击时记录 conflict position
- 把 conflict position 合并成 favorite minefield positions
- 周期性派 minelayer 去布雷
- 若无记录，则尝试在己方 minelayer 到随机敌人的路径中点布雷
- 发 `PlaceMinefield` 和回撤 `Move`

这个模块只在特定 AI 配置中开启，例如 RA turtle。

## 8 配置层：AI 性格主要来自 YAML

OpenRA 的 AI 策略不是硬编码成多个 C# 子类。C# 提供能力模块，YAML 负责组合。

以 RA 为例：

- `ModularBot@RushAI / NormalAI / TurtleAI / NavalAI` 定义 bot 类型
- `GrantConditionOnBotOwner` 为 bot type 授予 condition
- 每个模块按 `RequiresCondition` 激活
- 不同 bot type 各自配置：
  - `BuildingFractions`
  - `BuildingLimits`
  - `BuildingDelays`
  - `UnitsToBuild`
  - `UnitLimits`
  - `UnitDelays`
  - `SquadSize`
  - `RushInterval`
  - `MinimumAttackForceDelay`
  - `ProtectionScanRadius`
  - expansion tolerances

这套模式的核心收益：

- 一个模块能服务多个游戏和多个 AI 性格
- mod 可以只改 YAML 形成差异
- C# 代码保持能力抽象，数值和偏好在数据层

核心代价：

- actor type 名称在多个配置段重复
- 策略耦合隐藏在字符串、比例和 condition 中
- 参数非常多，调试依赖经验和 bot debug

## 9 性能策略

OpenRA AI 里有大量手写性能控制，不是靠框架统一调度：

1. 随机初始 tick offset
   - 避免所有 AI 同一 tick 扫描。
2. 分批更新
   - ResourceMap 每次只更新一个 indice。
   - SquadManager 把 pending squad update 摊到多个 tick。
   - BaseBuilder 每次只 tick 一个有效 queue category。
3. 事件索引
   - `ActorIndex` 订阅 `World.ActorAdded` / `ActorRemoved`，维护常用 actor 集合。
4. 昂贵查询限流
   - Harvester 每 tick 最多做一次 `FindNextResource`。
   - SupportPower 找不到目标后 delayed rescan。
5. Order 限流
   - `ModularBot.MinOrderQuotientPerTick` 让 pending orders 分批发出。

这说明他们很清楚 RTS AI 的瓶颈不在“算法名字”，而在每 tick 全图扫描、寻路、目标过滤和 order spam。

## 10 存档与回放

多个模块实现 `IGameSaveTraitData`：

- `BaseBuilderBotModule`
- `UnitBuilderBotModule`
- `SquadManagerBotModule`
- `McvManagerBotModule`
- `SupportPowerBotModule`
- `PowerDownBotModule`

它们会保存内部 tick、队列、squad、base center 等状态。

但 replay 中不启用 bot 决策；replay 只需要记录过的 order。这再次说明 OpenRA 把 AI 内部状态和世界同步状态分开处理。

## 11 调试与可视化

OpenRA 有简单但直接的 AI 调试能力：

- `AIUtils.BotDebug(...)`
- `Game.Settings.Debug.BotDebug`
- `Game.Settings.Debug.SyncCheckBotModuleCode`
- `OpenRA.Mods.Common/Traits/Render/RenderDebugState.cs`

`RenderDebugState` 会显示 actor 所属 AI squad 的类型和目标信息。这种“直接把 AI 内部状态画在单位上”的调试方式，对 RTS AI 特别有效。

## 12 设计优点

1. AI 不直接改世界状态
   - 保证 multiplayer/replay/debug 都围绕 order 链路。

2. AI 与玩家共享能力入口
   - AI 用 `AttackMove`、`Harvest`、`StartProduction`、`PlaceBuilding`，不会绕开 gameplay trait。

3. Bot 类型是数据组合
   - C# 模块复用，YAML 决定 Rush/Normal/Turtle/Naval。

4. 模块职责相对清晰
   - 经济、建筑、生产、采矿、扩张、作战、支援技能都独立。

5. Player actor 是天然 AI host
   - 玩家级策略状态不散落到单位身上。

6. 性能策略贴近 RTS 实际
   - 分帧、缓存、索引、限流，而不是每 tick 大脑全量重算。

7. 支持不同 mod
   - Common 模块通过 actor names、traits、target types 适配 RA/CnC/D2k/TS。

## 13 设计代价与风险

1. 字符串配置高度分散
   - `harv`、`proc`、`fact` 等 actor names 在多个模块配置里重复。
   - 优点是 mod 灵活，缺点是缺少强约束。

2. 模块内部启发式很厚
   - `BaseBuilderQueueManager` 和 `McvExpansionManager` 都包含大量策略、放置、特例和 retry 逻辑。

3. 没有统一 AI blackboard
   - `ResourceMapBotModule` 很像局部 blackboard，但不是通用设施。
   - 模块之间靠接口互调，读源码时需要追踪调用关系。

4. 没有统一决策解释模型
   - BotDebug 有用，但不是结构化 decision trace。

5. Campaign AI 与 skirmish AI 是两套世界
   - 通用 bot modules 管 skirmish。
   - 战役脚本大量 Lua，灵活但不统一。

6. 多处存在 HACK/TODO
   - 例如 building placement、D2k repair 特例、squad stuck fallback、raw coordinate transform 等。
   - 这些不是坏事，但说明这套系统是长期演化出来的实战工程，不是干净教科书架构。

## 14 对 Ludots 的借鉴

结合 Ludots 的“六边形架构、一切皆 Mod、禁止跨越职责”的方向，OpenRA 最值得借鉴的是边界，而不是直接照搬模块代码。

### 14.1 借鉴一：AI 只产生命令，不直接写世界

OpenRA 的最强原则是：

```text
AI Decision -> Order -> 正式 order/activity/system 链路 -> World mutation
```

如果 Ludots 后续做 RTS/战略 AI，应把 AI Runtime 定义为“正式输入/指令管线的生产者”，而不是让 AI system 直接改组件。

对应 Ludots 思路：

- AI 读 ECS snapshot / query
- AI 产生 formal command / order / intent
- order 进入现有输入、能力或命令管线
- 正式 system group 执行状态变化

### 14.2 借鉴二：Player/Commander 级 AI host

OpenRA 把 skirmish AI 挂在 `SystemActors.Player`。Ludots 如果做战略 AI，也应该优先考虑“玩家/阵营/指挥者实体”作为 AI host，而不是给每个单位塞一个全局策略脑。

单位级行为可以保留局部自动化，但战略层状态应归属阵营级 entity/mod。

### 14.3 借鉴三：模块组合，而不是 bot 子类继承树

OpenRA 的 Rush/Normal/Turtle/Naval 都复用同一套模块。差异来自：

- condition
- module enable
- ratio
- delay
- limit
- scan radius
- target type

Ludots 可用 Mod 配置或 Registry 描述：

- AI module capability
- AI profile
- module enable condition
- decision weight / budget
- command output type

### 14.4 借鉴四：模块间只暴露窄接口

OpenRA 的 `IBotRequestUnitProduction`、`IBotPositionsUpdated`、`IBotSuggestRefineryProduction` 很朴素，但有效。

Ludots 若要做 AI 模块，应避免模块互相直接拿内部字段，可以用明确的 port/interface/event：

- request production
- update base center
- suggest expansion location
- report idle army
- request defense response

### 14.5 借鉴五：性能预算内建进模块

OpenRA AI 到处可见：

- staggered tick
- one expensive search per tick
- pending update stacks
- actor indices
- delayed rescan
- order throttling

Ludots 的 AI Runtime 如果进入大规模模拟，应该从第一版就有：

- per-module tick budget
- query cache
- spatial index
- delayed scan schedule
- command output cap
- debug counters

### 14.6 谨慎点：不要照搬 GPL 代码

OpenRA 是 GPL 项目。本报告只做架构分析和思想参考，不应把 OpenRA 源码复制进 Ludots。若后续实现，应基于 Ludots 现有 ECS、Mod、ConfigPipeline、AI Runtime 和正式 order/ability 管线重新设计。

## 15 如果给 Ludots 设计一个对应骨架

以下不是实现方案，只是把 OpenRA 的经验翻译成 Ludots 风格时的候选结构。

```text
Faction/Commander Entity
  Components:
    AiProfileRef
    AiBlackboardHandle
    AiCommandQueue
    AiBudget

AiRuntime SystemGroup
  1. Snapshot collection
  2. Strategic module ticks
  3. Tactical module ticks
  4. Command proposal merge
  5. Command validation
  6. Dispatch into formal order/input/ability pipeline

Modules
  EconomyModule
  ProductionModule
  BasePlannerModule
  ResourcePlannerModule
  ArmySquadModule
  DefenseResponseModule
  SupportAbilityModule

Boundary
  AI modules cannot mutate gameplay components.
  AI modules emit validated command intents only.
```

这与 OpenRA 的本质一致，但要用 Ludots 自己的 ECS/Registry/Pipeline/Mod 设施承载。

## 16 关键源码路径清单

入口与激活：

- `external/reference/OpenRA/OpenRA.Game/Player.cs`
- `external/reference/OpenRA/OpenRA.Game/Traits/TraitsInterfaces.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/Traits/Player/ModularBot.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/Traits/Player/DummyBot.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/Traits/World/CreateMapPlayers.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/ServerTraits/SkirmishLogic.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/Widgets/Logic/Lobby/LobbyUtils.cs`

订单与校验：

- `external/reference/OpenRA/OpenRA.Game/World.cs`
- `external/reference/OpenRA/OpenRA.Game/Network/OrderManager.cs`
- `external/reference/OpenRA/OpenRA.Game/Network/UnitOrders.cs`
- `external/reference/OpenRA/OpenRA.Game/Actor.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/Traits/World/ValidateOrder.cs`

模块接口与工具：

- `external/reference/OpenRA/OpenRA.Mods.Common/TraitsInterfaces.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/AIUtils.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/ActorIndex.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/Traits/Conditions/GrantConditionOnBotOwner.cs`

BotModules：

- `external/reference/OpenRA/OpenRA.Mods.Common/Traits/BotModules/ResourceMapBotModule.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/Traits/BotModules/BaseBuilderBotModule.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/Traits/BotModules/BotModuleLogic/BaseBuilderQueueManager.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/Traits/BotModules/UnitBuilderBotModule.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/Traits/BotModules/HarvesterBotModule.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/Traits/BotModules/McvManagerBotModule.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/Traits/BotModules/McvExpansionManagerBotModule.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/Traits/BotModules/SquadManagerBotModule.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/Traits/BotModules/Squads/Squad.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/Traits/BotModules/Squads/StateMachine.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/Traits/BotModules/Squads/AttackOrFleeFuzzy.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/Traits/BotModules/Squads/States/GroundStates.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/Traits/BotModules/Squads/States/AirStates.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/Traits/BotModules/Squads/States/ProtectionStates.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/Traits/BotModules/SupportPowerBotModule.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/Traits/BotModules/BotModuleLogic/SupportPowerDecision.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/Traits/BotModules/BuildingRepairBotModule.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/Traits/BotModules/PowerDownBotManager.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/Traits/BotModules/CaptureManagerBotModule.cs`
- `external/reference/OpenRA/OpenRA.Mods.Common/Traits/BotModules/BotModuleLogic/MinelayerBotModule.cs`

代表配置：

- `external/reference/OpenRA/mods/ra/rules/ai.yaml`
- `external/reference/OpenRA/mods/cnc/rules/ai.yaml`
- `external/reference/OpenRA/mods/d2k/rules/ai.yaml`
- `external/reference/OpenRA/mods/ts/rules/ai.yaml`

## 17 总结

OpenRA AI 的底层系统是一套实用、模块化、命令驱动的 RTS AI 架构：

- Bot 是 player-level trait。
- AI 类型由 YAML 组合。
- 逻辑模块通过小接口协作。
- 决策只发 order。
- world mutation 仍走正式 order/activity/trait 链路。
- 性能靠分帧、索引、延迟扫描和限流。
- 战术层用轻量状态机和局部 fuzzy 判断。
- 战略层多是启发式和评分，而不是统一 planner。

它最值得 Ludots 学的不是具体算法，而是“AI 作为正式命令管线的外部输入者”这个边界。只要守住这条边界，AI 再复杂也不会跨越玩法系统职责；如果越过这条边界，AI 很快会变成第二套隐藏 gameplay runtime。
