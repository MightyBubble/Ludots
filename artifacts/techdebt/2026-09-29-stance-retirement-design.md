# 敌我"立场"退役：只剩关系

## 1. 概述

引擎里"谁是敌人、谁是盟友"现在有三套说法，互相靠手工对照表翻译：

| 现有说法 | 在哪 | 问题 |
|---|---|---|
| 立场查询 `DomainStanceQuery` + `assets/Relationships/projection.json` 的 `stance` 段 | Core | 本身就是关系边，额外加了"同方算友好""同队算友好""查不到算中立""多条边取第一条"四条暗规则 |
| 静态队伍表 `TeamManager` / `TeamRelationship` 枚举 / `TeamConfig` | Core | 关系之外的第二份事实；引擎启动时把"没写的队伍对"默认成敌对；存档另存一份 |
| 目标筛选枚举 `RelationshipFilter`（All/Hostile/Friendly/Neutral/NotFriendly/NotHostile）+ 图里两套编号 | Core | "敌对/友好"这些词写死在 C# 里；图的两处编号敌友对调 |

这次三套全部删掉。敌我只剩一种事实：**两个队伍代表实体之间有没有某种关系边**。关系类型名由数据声明，Core 不认识任何一个名字。

不在这次范围：战斗姿态 `CombatStance`（"固守 / 主动出击 / 停火"这类单位行为模式）和 AI 的 `AI/stances.json`。它们是"单位怎么打"，不是"谁是敌人"。战斗姿态 mod 判断敌人本来就是查关系边（`HasLink(我方队伍, 对方队伍, CombatStance.Hostile)`），这次就是把它的做法推广到全引擎。

## 2. 结构

```text
数据（mod 声明）                      Core（只认编号）                   使用方
─────────────────                    ─────────────────                 ─────────
Relationships/catalog.json           RelationshipTypeRegistry          技能效果目标筛选
  types: Hostile / Friendly / ...  →   名字 → 编号（装载期）           投射物碰撞筛选
Maps/*.json                          RelationshipRuntime               目标解析扇出
  ParticipantRelationships.Teams     队伍代表之间的关系边               实体集合查询
  {TeamA, TeamB, TypeId, Symmetric}→ TeamRelationQuery                 图节点 QueryFilterRelationship
MassNavigationConfig.json            队伍编号 → 代表实体 → 查边          AI 目标筛选
  teamRelationships[]                                                 命令来源资格
效果/AI/命令/图里的筛选字段          RelationFilter（装载期编译）       命令意图规则
  "Hostile" / "Friendly" / "All"      All 或 一个关系类型编号            群体导航"合作"判定
                                                                      呈现层敌我着色
```

- `TeamRelationQuery`：Core 服务，唯一的"敌我"查询入口。`Has(源队伍, 目标队伍, 关系类型)` 就是查两个队伍代表实体之间有没有这条边。
- `RelationFilter`：装载期把作者写的字符串编译成"不筛选"或"一个关系类型编号"，热路径只比整数。
- `Hostile`、`Friendly` 两个类型由 `mods/LudotsCoreMod/assets/Relationships/catalog.json` 声明，不放进引擎默认目录 `assets/Relationships/catalog.json`：Core 默认资源按 RFC-0065 不带"敌对 / 盟友"这类场景词。Core 代码也不引用这两个名字。mod 可以声明自己的类型（比如战斗姿态的 `CombatStance.Hostile`），筛选里直接写那个名字。

## 3. 详情

### 3.1 删除清单

| 删 | 替代 |
|---|---|
| `DomainStanceQuery`、`DomainStanceConfig`、`RelationshipCatalogConfig.Stance`、`assets/Relationships/projection.json` 及其目录登记、`CoreServiceKeys.DomainStanceQuery` | `TeamRelationQuery` |
| `TeamManager`、`TeamRelationship`、`TeamConfig`、`RelationshipEntry`、`TeamRelationshipSnapshot`、`MapSession.TeamRelationships`、`ParticipantBindingResult.TeamRelationships`、存档里的队伍关系段、`GameEngine` 里"默认敌对" | 关系边本身（存档已经保存关系边） |
| `RelationshipFilter`、`RelationshipFilterUtil`、`GraphRelationshipFilterMode`、`GraphRelationship` 常量、`IGraphRuntimeApi.GetRelationship`、图编译器的字符串→编号表、图执行器的编号→枚举表 | `RelationFilter` + 符号补丁把关系类型名换成编号 |
| 地图 `ParticipantRelationships.*.Attitude` 字段 | 队伍条目的 `TypeId` 直接写关系类型 |
| 立场桥接的双写（`ParticipantBindingResolver.ResolveStanceType`） | 无 |
| `CommandSourceEligibility` 里写死的 `"Hostile"`、`CoreInputMod` 本地命令里写死的 `RelationshipFilter.Hostile` | 命令来源数据里的 `relationFilter` |

### 3.2 查询规则（唯一一条）

`RelationFilter` 通过，当且仅当：

1. 筛选是 `All`：直接通过；
2. 否则：源、目标都有队伍（`Team.Id > 0`），并且"源队伍代表 → 目标队伍代表"有这个类型的关系边。

- 没有队伍（没有 `Team` 组件或 `Team.Id == 0`）就是"没有关系"，要求某种关系的筛选不通过。这是事实，不是兜底。
- `Team.Id > 0` 却找不到队伍代表实体：数据错误，直接抛异常。
- 同队、自己：不再有暗规则。想让"友好"包含自己队伍，地图里写 `TeamA == TeamB` 的 `Friendly` 自边。现有地图大多已经写了。
- 反向筛选（"非友好""非敌对"）和"中立"：现有数据没有一处用到，这次不提供。"中立"就是两边之间既没有敌对边也没有友好边。

### 3.3 作者写法

| 位置 | 旧 | 新 |
|---|---|---|
| 地图队伍关系 | `{"TeamA":1,"TeamB":2,"TypeId":"RtsDemo.Participant","Attitude":"Hostile"}` | 原条目去掉 `Attitude`、`TypeId` 不变（参与关系照旧存在），另加一条 `{"TeamA":1,"TeamB":2,"TypeId":"Hostile","Symmetric":true}` |
| 地图玩家关系 / 玩家-队伍关系 | 带 `Attitude` | 去掉 `Attitude`，`TypeId` 不变 |
| 效果 / 投射物 / AI / 命令来源筛选 | `"Hostile"` / `"Friendly"` / `"All"` | 写法不变，含义变成"关系类型名或 All"，名字必须已在关系目录里声明 |
| 自动选目标（技能 `input`、输入映射） | `"autoTargetPolicy": "NearestEnemyInRange"` | `"autoTargetPolicy": "NearestInRange"` + 必填 `"autoTargetRelation": "Hostile"`（光标选目标同理：`cursorTargetRelation`） |
| 命令意图规则 | `"target": {"stance": ["Hostile"]}` | `"target": {"relation": ["Hostile"]}` |
| 图节点 `QueryFilterRelationship` | `"relationshipMode": "Hostile"` | `"relationshipType": "Hostile"`，走现有关系类型符号补丁 |
| 群体导航配置 | `teamRelationships: {defaultRelationship, relationships[{teamA,teamB,attitude}]}` + `relationshipPolicy.cooperativeStance` | `teamRelationships: [{teamA,teamB,relation,symmetric}]`（没有默认）+ `relationshipPolicy.cooperativeRelation` |
| 代码里设置队伍关系（showcase 触发器） | `TeamManager.SetRelationshipSymmetric(1, 2, Hostile)` | 挪进地图数据；地图没有队伍绑定的，在触发器里对队伍代表调用 `RelationshipRuntime.EnsureLink` |

地图装载器对未知字段不报错，遗留的 `Attitude` 会被悄悄忽略，所以加一条守卫测试：任何地图 JSON 出现 `Attitude` 直接失败。

### 3.4 呈现层敌我着色

`PresentPhaseInput/Result` 去掉 `TeamRelationship`，改成 `IsFriendly` / `IsHostile` 两个布尔值，由呈现层的"观察者队伍 → 目标队伍"关系查询得出。用哪两个关系类型算"友好 / 敌对"，由 `game.json` 的 `presentation.teamRelationColors`（`friendlyRelation` / `hostileRelation`）声明，LudotsCoreMod 写的是 `Friendly` / `Hostile`。引擎据此注册 `PresentTeamRelationClassifier` 服务，只有表现体着色用它；世界 HUD 的可读性只看知识投影，不看敌我。

### 3.5 性能

- 热路径（技能目标筛选、投射物碰撞、AI 候选、实体集合查询）只做两次整数字典查找（队伍编号 → 代表实体）和一次关系边查找，不分配内存，不改结构。以前是一次 `Dictionary<long, …>` 查找，量级相同。
- 名字 → 编号只在装载期做一次。

## 4. 场景

| 场景 | 旧行为 | 新行为 |
|---|---|---|
| 火球只打敌人 | 队伍表说 1、2 敌对 | 地图写了 1→2 `Hostile` 边 |
| 治疗只加自己人 | 同队暗规则算友好 | 地图写了 1→1 `Friendly` 自边 |
| 地图忘了写 1、2 关系 | 引擎默认敌对，照样开打 | 两队互不识别为敌人；技能打不到，验收会暴露 |
| 群体导航同队互相让路 | 同方暗规则算友好 | 配置写了同队 `Friendly` 自边，`cooperativeRelation: Friendly` |
| 右键敌方单位 = 攻击 | 意图规则 `stance: ["Hostile"]` | `relation: ["Hostile"]` |
| 战斗姿态"只还击敌人" | 查 `CombatStance.Hostile` 边 | 不变 |

## 5. 边界

- 只查队伍级关系。玩家之间的关系边（`ParticipantRelationships.Players`）照旧存在，给外交、社交这类玩法用，不参与敌我筛选。
- 关系是有方向的：1→2 敌对不等于 2→1 敌对。地图条目 `Symmetric: true` 就是写两条边，和以前一致。
- 不做关系缓存。需要时再按关系边修订号做失效缓存，不另立概念。
- 旧存档里的队伍关系段不再读取（仓库规则：不做向后兼容）。
- `CombatStance`、`AI/stances.json` 保持原样，见概述。

## 6. UAT

```gherkin
功能: 敌我只由关系决定

  场景: 火球只烧敌人
    假如 我在冠军技能沙盒里操作蓝方英雄
    并且 地图声明了蓝方对红方是"敌对"关系
    当 我把火球扔进红蓝两方单位混在一起的人堆
    那么 只有红方单位掉血
    并且 蓝方单位血量不变

  场景: 治疗只加自己人
    假如 我在自动施法展示里看着蓝方牧师
    并且 地图声明了蓝方对蓝方自己是"友好"关系
    当 一个蓝方单位受伤
    那么 牧师给这个蓝方单位加血
    并且 牧师从不给红方单位加血

  场景: 地图漏写关系就打不起来
    假如 一张地图只声明了两支队伍，没写它们之间的关系
    当 我让蓝方单位对红方单位放"只打敌人"的技能
    那么 技能找不到目标
    并且 不会因为引擎默认把他们当敌人而打起来

  场景: 右键敌人就是攻击
    假如 我在多人前线展示里选中自己的步兵
    当 我右键点一个红方单位
    那么 步兵收到攻击命令
    当 我右键点一个自己人
    那么 步兵不会收到攻击命令

  场景: 群体行军同队互相让路
    假如 群体导航展示里两支蓝方小队在窄路口相遇
    并且 配置声明了蓝方对蓝方是"友好"，合作关系用"友好"
    当 它们同时通过路口
    那么 两队互相让路，不互相顶撞卡死

  场景: 敌我着色跟着关系走
    假如 我以蓝方视角观看战场
    那么 蓝方单位的血条是友方色
    并且 红方单位的血条是敌方色
    并且 没有任何关系的中立单位用默认色
```
