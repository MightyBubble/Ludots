# 关系与控制面图化设计（Case E 风格）

状态：待评审，未开发。

前提：下令部分以 `cursor/tw-showcase-intent-members-de53` 分支的合同为准。那份合同里交互状态不再声明集合键（`activeCollectionKey` 已删，写了启动即失败），下令图自己装入这一次的成员，随意图一起提交；下令的后续处理只做授权和逐个下单。

## 1. 概述

"谁是谁的主人""我能指挥谁"这类问题，照 Case E 选人名单的做法来答：

- **真相只有关系边。** 城堡归谁，就看有没有一条"某玩家 →Owns→ 城堡"的边。引擎不在单位身上另存一份归属。
- **规矩写在改边的那张图里。** "一个城堡只能有一个主人"不是引擎规矩，也不是关系目录里的配置，是"夺城"这张图自己的写法：先断旧主人，再连新主人。别的玩法要别的规矩，就写别的图。
- **反复要用的答案放进集合。** "我能指挥谁"由一张图从玩家实体出发、沿关系边查出来，写进挂在这个玩家实体上的集合 `rts.commandable`。它和 Case E 的候选集合 `case_e.selectable` 是同一种东西。框选只能从这个集合里选人；下令图从 `selected` 装入成员提交。
- **配置只写接线。** 关系目录只声明有哪些关系名；交互状态的配置只写挂哪些图；集合名写在图和交互状态里。引擎代码里不出现任何关系名。

这一轮不新增配置结构。引擎侧只补 4 个小缺口（见 3.5），每个都是给现有节点或事件补能力，不是新管线。

## 2. 结构

```text
玩法动作（夺城 / 召唤 / 精神控制 / 盟友掉线）
  └─ 改边的图：用现有节点 RelationshipRemoveLink / RelationshipEnsureLink 改关系边
        │  （改边后引擎照常发 RelationLinkAdded / RelationLinkRemoved 事件）
        ▼
每个玩家实体上挂的同步图 rts.commandable_sync
  └─ 听：关系边变化（只听 Owns / Controls）、单位出生、单位死亡、地图加载
  └─ 做：调查询图 → 把结果写进这个玩家的集合 rts.commandable
        ▼
选人：候选集合 = rts.commandable（Case E 候选名单那一套不变）
下令：下令图 QueryFromCollection(selected) 装入成员 → SubmitCommandIntent / SubmitCast 提交
授权：不单独做。成员来自玩家实体上的集合，而集合只装能指挥的（见 3.6）
表现：presenter 订阅集合成员变化（蓝环 / 归属色），不自己判断归属
```

| 层 | 负责 | 配置落点 | 不知道什么 |
| --- | --- | --- | --- |
| 关系词汇 | 有哪些关系名 | `Relationships/catalog.json` 的 `types[]`（只有 `id`、`isSymmetric`） | 谁是主人、有几个主人 |
| 改边 | 某个玩法动作怎么改关系 | 效果图 / 事件图（`GAS/graphs/*.json`、`GAS/effects.json` 的 `phaseGraphs`） | 集合、选人 |
| 查询 | 从玩家出发沿边查出"我能指挥的" | 纯查询图（`kind: Query`） | 边是谁改的 |
| 同步 | 边变了就重写集合 | 挂在玩家交互状态上的事件图（`Input/interaction_context_profiles.json` 的 `triggers[]`） | 具体玩法 |
| 消费 | 选人、下令、表现 | 现有 Case E 链路 | 关系边 |

## 3. 详情

### 3.1 关系目录：只剩关系名

```json
{
  "types": [
    { "id": "Owns",     "isSymmetric": false },
    { "id": "Controls", "isSymmetric": false },
    { "id": "MemberOf", "isSymmetric": false }
  ],
  "metrics": [],
  "flags": []
}
```

去掉本分支加的 `role` 和 `rules`。`template`、`metrics`、`flags` 是原有功能，不动。

### 3.2 共用部分：每个玩家的"我能指挥的"集合

**查询图 `rts.owned_query`**（`kind: Query`，入口是玩家实体）

| 步骤 | 节点 | 现有 / 缺口 |
| --- | --- | --- |
| 1 | `LoadCaster`：玩家实体 | 现有 |
| 2 | `RelationshipExpandDescendants`（`relationshipType: "Owns"`）：沿 Owns 往下所有层，返回全部后代，不含玩家自己 | **缺口 G2** |
| 3 | `QueryFilterSelectable`：只留可选单位（城堡里的物资、背包里的剑不进名单） | 现有 |

**查询图 `rts.granted_query`**（`kind: Query`）：盟友交给我代管的单位

| 步骤 | 节点 | 现有 / 缺口 |
| --- | --- | --- |
| 1 | `LoadCaster`：玩家实体 | 现有 |
| 2 | `RelationshipQueryOutgoing`（`Controls`）：我被授权代管的盟友玩家 | 现有 |
| 3 | `RelationshipExpandDescendants`（`Owns`）：这些盟友名下所有层 | **缺口 G2** |
| 4 | `QueryFilterSelectable` | 现有 |

**同步图 `rts.commandable_sync`**（`kind: TriggerGraph`）

```json
{
  "id": "rts.commandable_sync",
  "kind": "TriggerGraph",
  "entries": [
    { "label": "on_map_loaded",   "event": "MapLoaded",          "start": "rep", "refire": "restart" },
    { "label": "on_owns_added",   "event": "RelationLinkAdded",  "start": "rep", "refire": "restart", "filters": { "relationshipType": "Owns" } },
    { "label": "on_owns_removed", "event": "RelationLinkRemoved","start": "rep", "refire": "restart", "filters": { "relationshipType": "Owns" } },
    { "label": "on_ctrl_added",   "event": "RelationLinkAdded",  "start": "rep", "refire": "restart", "filters": { "relationshipType": "Controls" } },
    { "label": "on_ctrl_removed", "event": "RelationLinkRemoved","start": "rep", "refire": "restart", "filters": { "relationshipType": "Controls" } },
    { "label": "on_unit_spawned", "event": "EntitySpawned",      "start": "rep", "refire": "restart" },
    { "label": "on_unit_died",    "event": "EntityDied",         "start": "rep", "refire": "restart" }
  ]
}
```

节点链：`LoadExplicitTarget`（玩家实体）→ `InvokeGraph(rts.owned_query)` → `WriteCollection(rts.commandable, replace)` → `InvokeGraph(rts.granted_query)` → `WriteCollection(rts.commandable, add)` → 把 `selected` 里已经不归我管的减掉（三步，见 3.6）。

- `filters.relationshipType` 是**缺口 G3**；其余字段、节点都是现有的。
- 单位死亡时引擎不发"关系断开"事件（死掉的一端在查询时跳过），所以同步图也要听 `EntityDied`，和 RTS 现有的 `graph.rts.roster_sync` 一样。

**挂载**：玩家的常驻交互状态

```json
{
  "id": "interaction.context.rts.battle",
  "bindings": [ "CaseE.BoxSelectBegin", "Rts.Command" ],
  "triggers": [
    { "trigger": "rts.commandable_sync" },
    { "trigger": "rts.box_begin" },
    { "trigger": "rts.command" }
  ],
  "commandIntentId": "intent.command.default"
}
```

字段都是 tw 分支上现有的：`bindings`、`triggers`、`commandIntentId`。没有集合键。

选人候选直接用 `rts.commandable`：框选命中图的输入从 `QueryFromCollection(rts.commandable)` 取，和 Case E 的 `box_hit` 从 `case_e.selectable` 取一样。

下令图 `rts.command` 照 tw 的 `tw.rts.command` 写：`LoadCaster` → 指针落地 → `QueryFromCollection(selected)` 装入成员 → `SubmitCommandIntent`。本设计不改下令图。

对比现状：Case E 的候选图 `graph.case_e.candidates` 写死了 `QueryFilterTeam(teamId: 1)`，第二个玩家要复制一份 `graph.case_e.roster_sync_player2`。改成沿关系边查以后，一张同步图挂在每个玩家实体上，各算各的，不用按玩家复制图。

### 3.3 四个场景的改边图

#### 场景一：夺城（像《要塞》《帝国时代》）

初始边：`红方玩家 →Owns→ 城堡`，`城堡 →Owns→ 驻军 ×6`，`城堡 →Owns→ 城头炮台 ×2`。

占领条件由这个玩法自己决定（比如在城门区域站满若干秒）。条件达成时，这个玩法的图发一个自定义事件：

```json
[
  { "id": "rts.castle_captured", "scope": "map",
    "params": [
      { "name": "capturer", "type": "entity", "key": "Rts.Capturer" },
      { "name": "castle",   "type": "entity", "key": "Rts.Castle" }
    ] },
  { "id": "rts.capture_rejected", "scope": "map",
    "params": [
      { "name": "castle",   "type": "entity", "key": "Rts.Castle" }
    ] }
]
```

两个都写在这个玩法 mod 的 `Events/custom_events.json`。

**夺城图 `rts.capture`**（`kind: TriggerGraph`，`event: rts.castle_captured`）

| 步骤 | 节点 | 现有 / 缺口 |
| --- | --- | --- |
| 1 | `LoadEntryPayloadEntity(Rts.Castle)`、`LoadEntryPayloadEntity(Rts.Capturer)` | 现有 |
| 2 | `RelationshipQueryIncoming(Owns)`：城堡现在的主人 | 现有节点，**缺口 G1**：目前只能在纯查询图里用 |
| 3 | `AggCount` + `CompareEqInt(1)` + `JumpIfFalse`：主人必须正好 1 个；不是 1 个就走 `DispatchMapEvent(rts.capture_rejected)` 发出"占领未生效"事件（点名城堡），然后停下，不改任何边 | 现有 |
| 4 | `TargetListGet(0)` → `RelationshipRemoveLink(旧主人, 城堡, Owns)` | 现有 |
| 5 | `RelationshipEnsureLink(占领者, 城堡, Owns)` | 现有 |

驻军和炮台不用动：它们挂在城堡下面，城堡换了主人，蓝方的 `rts.owned_query` 往下走自然就能走到它们，红方就走不到了。

#### 场景二：召唤（像《暗黑破坏神》死灵法师）

死灵法师召出骷髅法师，骷髅法师再召小骷髅。边：`玩家 →Owns→ 死灵法师 →Owns→ 骷髅法师 →Owns→ 小骷髅`。

召唤技能照常用 `SpawnTemplate` 出兵。**召唤连边图 `arpg.summon_link`**（`kind: TriggerGraph`）

```json
{ "label": "on_summoned", "event": "EntitySpawned", "start": "spawned", "refire": "restart",
  "filters": { "payload": { "MapTrigger.SpawnTemplate": "arpg_skeleton_mage" } } }
```

每种召唤物一个入口（小骷髅再加一条 `arpg_skeleton_minion`）。子弹、特效这类出生不连边，因为没有入口听它们。

| 步骤 | 节点 | 现有 / 缺口 |
| --- | --- | --- |
| 1 | `LoadEntryPayloadEntity(MapTrigger.SourceEntity)`：刚出生的骷髅 | 现有 |
| 2 | `LoadEntryPayloadEntity(MapTrigger.SpawnerEntity)`：是谁召的 | **缺口 G4** |
| 3 | `RelationshipEnsureLink(召唤者, 骷髅, Owns)` | 现有 |

入口过滤用的是现有的 `filters.payload`（按字符串比对），但出生事件现在只带 `sourceEntity` 和 `sourceTeamId`，模板名和召唤者都没有，所以 G4 要给出生事件补这两项。

召唤物跨了几层，`rts.owned_query` 都能走到，玩家都能框选、下令。

#### 场景三：精神控制（像《星际争霸》黑暗执政官、Dota 的支配）

对敌方单位施放精神控制：30 秒内归我，时间到还给原主人。这个场景里普通单位直接挂在玩家实体下面（`敌方玩家 →Owns→ 坦克`），所以原主人一定是玩家实体，对局中不会消失。

效果配置（现有结构）：

```json
{
  "id": "Effect.Rts.MindControl",
  "presetType": "Buff",
  "lifetime": "After",
  "duration": { "durationTicks": 1800, "periodTicks": 0, "clockId": "FixedFrame" },
  "phaseGraphs": {
    "OnApply":  { "main": "Graph.Rts.MindControl.Take" },
    "OnExpire": { "main": "Graph.Rts.MindControl.Return" },
    "OnRemove": { "main": "Graph.Rts.MindControl.Return" }
  }
}
```

**夺取 `Graph.Rts.MindControl.Take`**

| 步骤 | 节点 | 现有 / 缺口 |
| --- | --- | --- |
| 1 | `LoadCaster`（施法英雄）、`LoadExplicitTarget`（敌方单位） | 现有 |
| 2 | `RelationshipQueryIncoming(Owns)` → 主人必须正好 1 个 → `TargetListGet(0)` | **缺口 G1** |
| 3 | `WriteBlackboardEntity(target, "mind_control.previous_owner")`：记下原主人，记在被控单位自己身上 | 现有 |
| 4 | `RelationshipRemoveLink(原主人, 目标, Owns)` → `RelationshipEnsureLink(施法英雄, 目标, Owns)` | 现有 |

**归还 `Graph.Rts.MindControl.Return`**：`ReadBlackboardEntity(target, "mind_control.previous_owner")` → `RelationshipRemoveLink(施法英雄, 目标, Owns)` → `RelationshipEnsureLink(原主人, 目标, Owns)`。全部是现有节点。

施法英雄在 30 秒内死了也一样：到期时照常断开、连回原主人。

"一个单位只能有一个主人"在这里又出现一次，同样由图自己保证。

#### 场景四：盟友掉线代管（像《帝国时代》《魔兽争霸》的共享控制）

盟友掉线时，我可以指挥他的部队；他回来后收回。现有的 `Relationships/control_profiles.json` 已经能表达，一行都不用改：

```json
{
  "id": "profile.control.ally_offline_proxy",
  "when": { "all": [
    { "relationship": "Ally", "between": ["grantee", "grantor"] },
    { "tag": "participant.offline", "on": "grantor" }
  ]},
  "grant": { "edgeType": "Controls", "from": "grantee", "to": "grantor" },
  "revokeWhen": { "not": { "tag": "participant.offline", "on": "grantor" } }
}
```

连上或撤掉 `Controls` 边时，同步图收到事件，`rts.granted_query` 把盟友名下的单位并进我的 `rts.commandable`，或者从里面去掉。

### 3.4 要删掉的

| 删什么 | 原因 |
| --- | --- |
| 关系目录的 `role`、`RelationshipRoleBindings` | 引擎不需要知道哪种关系是"所有"；关系名只在图里出现 |
| 关系目录的 `rules`、`RelationshipTypeRuleRegistry`、`RelationshipRuntime.Rules.cs` | "唯一主人"等规矩写在改边的图里 |
| 连边时自动顶替旧主人、自动重算整棵子树归属 | 归属不再缓存，查询时现算，没有东西需要重算 |
| `OwnershipResolver` | 你之前要求删；它的活由改边的图和查询图接手 |
| 下令时的逐个成员授权 `InputOrderActorAuthorization`，以及 `ControlDomainQuery` | 能指挥谁由玩家实体上的集合回答，见 3.6 |

### 3.5 引擎要补的缺口（只有这 4 个）

| 编号 | 补什么 | 为什么现有东西拼不出来 | 有没有先例 |
| --- | --- | --- | --- |
| G1 | `RelationshipQueryIncoming` / `RelationshipQueryOutgoing` 除了纯查询图，也允许在效果图、脚本图、事件图里用 | 改边的图要先知道旧主人是谁；这两个节点现在只能在纯查询图里用，而 `InvokeGraph` 只能在事件图里调，效果图没法调 | 有。D15 把集合读取节点从"只能查询"放开到脚本和事件图，理由相同 |
| G2 | 新节点 `RelationshipExpandDescendants`：输入一份名单和一种关系，返回沿这种关系往下所有层能走到的实体，去重，不含起点；已经死掉的实体不算，也不再从它往下走；超出容量就报错点名，不截断 | 图里没有遍历名单的循环，多层归属（城堡 → 驻军、召唤物的召唤物）拼不出来 | 没有。这是唯一的新节点 |
| G3 | 事件入口的 `filters` 加 `relationshipType`：写关系名，挂载时换成编号，写错名字挂载时报错 | 关系变化事件里带的是关系类型编号；现有的 `filters.payload` 只能写死数字 | 有。`filters.tag` 就是按名字过滤 |
| G4 | 出生事件 `EntitySpawned` 的载荷加两项："召唤者"（`MapTrigger.SpawnerEntity`，实体）和"模板名"（`MapTrigger.SpawnTemplate`，字符串） | 出兵节点 `SpawnTemplate` 不返回新实体，出生事件也不带召唤者和模板，召唤连边图既不知道该从谁连，也分不出出生的是不是召唤物 | 出兵请求里本来就带召唤者和模板，只是没放进事件 |

### 3.6 下令不再单独授权

"这个玩家能不能指挥这个单位"不需要引擎另判一次，Case E 的现成零件已经保证了：

- **能框的只有能指挥的。** 框选命中图的候选来自玩家实体上的 `rts.commandable`，所以 `selected` 一开始就只装能指挥的单位。
- **归属变了，选中名单也跟着收。** 同步图重算完 `rts.commandable` 以后，接着把 `selected` 里已经不归我管的去掉。没有求交集的节点，用现有的三种写法拼：

| 步骤 | 节点 | 结果 |
| --- | --- | --- |
| 1 | `QueryFromCollection(selected)` → `WriteCollection(rts.lost, replace)` | `rts.lost` = 选中的 |
| 2 | `QueryFromCollection(rts.commandable)` → `WriteCollection(rts.lost, subtract)` | `rts.lost` = 选中了、但已经不归我管的 |
| 3 | `QueryFromCollection(rts.lost)` → `WriteCollection(selected, subtract)` | `selected` 只剩还归我管的 |

- **成员不从客户端来。** 下令图用 `QueryFromCollection(selected)` 从玩家实体上的集合装入成员，现有代码里没有把成员名单跨网络转发的逻辑，所以没有伪造的入口。

所以下令时逐个成员调的 `InputOrderActorAuthorization.IsAuthorized`（里面是 `ControlDomainQuery.IsControllableBy`）可以删掉。

`ControlDomainQuery` 在 C# 里还有这些调用，要逐个改成读玩家实体上的集合：

| 调用方 | 用它做什么 |
| --- | --- |
| `InputOrderActorAuthorization` | 下令时逐个成员授权（上面说的，删掉） |
| `CoreInputMod` 的 `LocalOrderSourceHelper` | 自动选目标时判断成员归哪个玩家；随 CoreInputMod 退役整个删掉，见 `2026-09-28-coreinputmod-retirement-design.md` 3.2 |
| `CollectionApplier.ReplaceRouted` | 按控制域把一份名单拆开，分别写进各玩家的集合 |
| `ControlPlaneProjectionShowcaseMod`、`FormationCapabilityShowcaseMod` | showcase 里判断归属 |

改完这些，`ControlDomainQuery` 本身删掉。

## 4. 场景

| 场景 | 玩家看到的 | 背后发生的 |
| --- | --- | --- |
| 夺城 | 蓝方占领后，城堡、驻军、炮台都能被蓝方框选和指挥；红方框不到了 | 夺城图断旧边、连新边；两方的同步图各自重算集合 |
| 召唤两层 | 死灵法师召出的骷髅法师、骷髅法师召出的小骷髅，玩家都能框选和指挥 | 召唤连边图逐层连边；查询图往下走到底 |
| 精神控制 | 敌方单位 30 秒内听我指挥，时间到或被驱散时回到原主人那边 | 效果生效时换边并记下原主人，到期或被驱散时换回来 |
| 精神控制中施法者阵亡 | 施法者死后到到期之前，这个单位谁也指挥不了；到期后回到原主人那边 | 施法者死了，它往下的边在查询时被跳过；到期时断边什么都不做，再连回原主人。施法者死后效果会不会照常到期，开发时要先核实 |
| 选中后归属变了 | 我选中了被精神控制的坦克，控制到期后它从我的选中里消失，右键不会再给它下令 | 同步图重算后把 `selected` 里不归我管的减掉 |
| 盟友掉线 | 盟友掉线后我能框他的兵；他回来后我框不到了 | 现有控制授权连上或撤掉 `Controls` 边，同步图把盟友的兵并进或移出我的集合 |
| 召唤者死亡 | 死灵法师死了，他召出的骷髅变成无主，谁也指挥不了 | 死掉的一端在查询时被跳过；同步图听到 `EntityDied` 后重算 |

## 5. 边界

- **引擎不防成环。** 这 4 个场景里，新主人要么是玩家实体（没有上级），要么是施法英雄（不会反过来被目标拥有），不会绕成环。以后哪个玩法可能绕成环，由它自己的改边图先查再连。
- **单位身上的"归属玩家"组件 `PlayerOwner` 这一轮不退役。** main 上还有这些地方在读它：订单里记的玩家编号、行为树宿主、效果应答链、批量出兵。夺城后驻军身上的这个组件仍然是红方。退役它要把这些读取点逐个改成读集合或读下令的玩家，单独出一份方案，不混进这一轮。这一轮验收只看"谁能框选、谁能下令"。
- **背包不在这一轮。** 背包里"容器归谁、物品在哪个容器"现在也用 Owns 边表达，写在 C# 的背包服务里。它的"在哪个容器"本身已经有位置组件记录，要不要继续用关系边，另行评估。
- **敌我立场（`projection.json` 的 `stance`）不在这一轮。** 它目前读 `MemberOf`，不经过 `OwnershipResolver`。
- **召唤者死后召唤物要不要一起死**，属于玩法选择，由那个玩法自己的图决定，不写进引擎。
- **图里没有"这个实体还活着吗"的判断节点。** 往死掉的实体连边会报错，所以这一轮的改边图只往玩家实体或当场在场的实体连边。精神控制的原主人如果可能是会死的单位（比如指挥官），归还前需要先判断存活，那时再补节点。
- **占领出错只发事件，不写日志。** 图里没有写日志或报错的节点，所以主人数不对时，夺城图发 `rts.capture_rejected` 事件并停下，由这个玩法决定怎么提示。
- **同步图是整份重写。** 每次关系变化都把这个玩家的集合整份重算一遍，和 RTS 现有候选名单的做法一样。人口到了上万级别，改成增量维护要另开一单（Case E 已记录同类债务）。
- 写集合全部走 `WriteCollection`，没有第二个写入口。`selected` 由选中落定图和同步图两处写，和 Case E 里 `selection.pending` 由松手图写、落定图消费是同一种用法。
- 关系边变了到同步图跑完之间，有一小段时间 `rts.commandable` 还是旧的。这段时间里下的令还按旧名单发。开发时要量一下这段时间有多长。

## 6. UAT

```gherkin
Feature: 占领城堡后城里的兵跟着换边

  Scenario: 蓝方占领红方城堡
    Given 红方有一座城堡，城里驻着 6 个步兵，城头有 2 座炮台
    And 红方玩家能框选这 6 个步兵和 2 座炮台
    When 蓝方英雄在城门区域站满占领时间
    Then 蓝方玩家框选城堡周围，能选中这 6 个步兵和 2 座炮台
    And 蓝方玩家右键地面，这 6 个步兵朝那里走
    And 红方玩家再框选同一片区域，一个也选不中

  Scenario: 城堡归属数据出错时不偷偷改
    Given 一座城堡因为别的玩法同时连着红方和黄方两个主人
    When 蓝方占领这座城堡
    Then 城堡的主人不变，红方和黄方仍然都能框选城里的兵
    And 蓝方框选不到城里的兵
    And 场上发出一条"占领未生效"事件，点名这座城堡

Feature: 召唤物听召唤者的玩家指挥

  Scenario: 召唤物再召唤
    Given 我的死灵法师召出一个骷髅法师
    And 骷髅法师又召出 3 个小骷髅
    When 我框选这一片
    Then 骷髅法师和 3 个小骷髅都被选中
    And 我右键敌人，它们都去攻击

  Scenario: 召唤者死亡
    Given 我的死灵法师召出一个骷髅法师
    When 死灵法师被敌人打死
    Then 我框选骷髅法师，选不中

Feature: 精神控制到时归还

  Scenario: 30 秒后回到原主人
    Given 我的执政官对一个敌方坦克施放精神控制
    Then 我能框选这个坦克并让它开火
    And 敌方玩家框选不到这个坦克
    When 30 秒过去
    Then 敌方玩家又能框选这个坦克
    And 我框选不到这个坦克

  Scenario: 施法者阵亡也按时归还
    Given 我的执政官对一个敌方坦克施放了精神控制
    And 执政官在 30 秒内被打死
    When 30 秒过去
    Then 敌方玩家又能框选这个坦克
    And 我框选不到这个坦克

  Scenario: 选中的坦克到期后不再听我的
    Given 我选中了自己的步兵和一个被我精神控制的敌方坦克
    When 精神控制到期
    Then 坦克的选中环消失
    And 我右键地面，只有步兵移动，坦克不动

  Scenario: 被驱散时立刻归还
    Given 我控制着敌方的一个坦克
    When 敌方对这个坦克施放驱散，精神控制被移除
    Then 敌方玩家马上又能框选这个坦克

Feature: 盟友掉线代管

  Scenario: 掉线时能指挥，回来后收回
    Given 我和黄方是盟友
    When 黄方玩家掉线
    Then 我框选黄方的步兵能选中，右键能让它们移动
    When 黄方玩家重新连上
    Then 我框选黄方的步兵，选不中
```
