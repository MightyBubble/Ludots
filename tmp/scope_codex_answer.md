# Activity 作用域与引擎绑定研究

## 1. 概述

本研究只覆盖 Activity 的作用域、实例归属、触发目标、DataPlane 投递和引擎缺口，不讨论投票人数、计票规则、平票或 quorum。

当前引擎的 Activity 是“实体归属”模型：

- 实例组件保存 `ScopeHost`：[`ActivityComponents.cs`](C:/001_AI/LudotsProd/src/Core/Gameplay/Activities/ActivityComponents.cs)
- 准入索引是 `(DefinitionId, ScopeKey)`：[`ActivityRuntimeService.cs`](C:/001_AI/LudotsProd/src/Core/Gameplay/Activities/ActivityRuntimeService.cs)
- 当前 `ScopeKey` 直接取 `scopeHost.Id`，空实体为 `0`
- TriggerGraph 的 `OfferActivity` 只接受一个 `Entity scopeHost`：[`IGraphRuntimeApi.cs`](C:/001_AI/LudotsProd/src/Core/NodeLibraries/GASGraph/IGraphRuntimeApi.cs)、[`GasGraphRuntimeApi.cs`](C:/001_AI/LudotsProd/src/Core/NodeLibraries/GASGraph/Host/GasGraphRuntimeApi.cs)

因此，v5 需要把“实体 host”提升为显式的“作用域引用”，同时保留实体作为实际执行上下文。

---

## 2. 结构

### 2.1 现有绑定关系

```text
MapSession
  ├─ MapConfig.Teams[]
  │    └─ TeamId → Team representative entity
  ├─ MapConfig.Players[]
  │    └─ PlayerId → Player representative entity
  └─ MapLaunchContext.LocalSeats[]
       └─ seatId → playerId → Player representative entity

ClientLocalSeatRegistry
  └─ seatId → possessed playerId / possessed representative
```

依据：

- `MapConfig` 已有 `Teams`、`Players`：[`MapConfig.cs`](C:/001_AI/LudotsProd/src/Core/Config/MapConfig.cs)
- `ParticipantBindingResolver` 将 `PlayerId`、`TeamId` 与代表实体建立查找表：[`ParticipantBindingResolver.cs`](C:/001_AI/LudotsProd/src/Core/Gameplay/Teams/ParticipantBindingResolver.cs)
- `PlayerEntityLookup`、`TeamEntityLookup` 已分别提供 ID 到 ECS 实体的解析：  
  [`PlayerEntityLookup.cs`](C:/001_AI/LudotsProd/src/Core/Gameplay/Teams/PlayerEntityLookup.cs)  
  [`TeamEntityLookup.cs`](C:/001_AI/LudotsProd/src/Core/Gameplay/Teams/TeamEntityLookup.cs)
- `MapLaunchContext.LocalSeats[]` 是进图座位事实，运行时由 `ClientLocalSeatRegistry` 持有：  
  [`MapLaunchContext.cs`](C:/001_AI/LudotsProd/src/Core/Map/MapLaunchContext.cs)  
  [`client-local-seat-and-logic-view.md`](C:/001_AI/LudotsProd/gitbook/architecture/client-local-seat-and-logic-view.md)

### 2.2 作用域分类学

| 作用域 | 业务含义 | 推荐 host | 稳定作用域键 |
|---|---|---|---|
| `per_representative` | 某个玩家代表某个玩家身份拍板 | `Players[].RepresentativeInstanceId` 对应实体 | `mapSession + representative/playerId` |
| `per_team` | 某个队伍共用一项活动 | `Teams[].RepresentativeInstanceId` 对应实体 | `mapSession + teamId` |
| `per_faction` | 某个派系共用一项活动 | 派系代表实体；不能默认等同 team | `mapSession + factionId` |
| `global` | 地图或整场会话唯一的世界事件 | `Entity.Null` 或专用全局 host | `session + global namespace` |

#### per-representative

这是当前模型最接近的形式。`Players[]` 已经把 `PlayerId` 和代表实体绑定，seat 再通过 `playerId` 找到该代表实体。

建议：

```text
seatId → playerId → player representative entity → Activity scope host
```

不能只依赖当前 seat 的 `PossessedRep` 作为持久身份，因为 possession 可以转移；Activity 的归属应绑定玩家/代表身份，seat 只负责当前输入和确认权限。

#### per-team

`TeamEntityLookup` 已是可复用的队伍代表解析器。Activity 实例可将队伍代表实体作为 `ScopeHost`，其选项脚本继续以该实体作为 `Caster`。

这能复用现有：

- `TeamId → team representative entity`
- `PlayerRep → teamRep` 的 `MemberOf` 关系
- `QueryFilterTeam` 和团队关系查询能力

#### per-faction

当前 Core 没有独立的 `FactionIdentity`、`FactionEntityLookup` 或 faction 绑定合同。仓库中的 “faction” 主要出现在 showcase 文案、标签和地形数据中，不能直接视为引擎级身份。

因此必须二选一：

1. 明确声明 `faction == team`，复用 `TeamEntityLookup`；
2. 新增派系身份、派系代表实体和 `FactionEntityLookup`。

建议 v5 不隐式把 faction 映射为 team。若暂时没有独立派系模型，加载器应拒绝 `per_faction`，而不是静默降级到 team。

#### global

全局 Activity 不应把 `Entity.Null` 当作普通实体 host 使用。`Entity.Null` 可以作为执行上下文占位，但作用域键必须由运行时显式生成。

建议区分：

- `map_global`：某个 `MapSession` 内唯一
- `application_global`：整个应用/对局会话唯一

当前 `MapSessionManager` 以 `MapId` 管理并发 session：[`MapSessionManager.cs`](C:/001_AI/LudotsProd/src/Core/Map/MapSessionManager.cs)。如果未来允许同一地图同时运行多个 session，仅使用 `MapId` 不足，需要稳定的 `MapSessionId`。

---

## 3. 全局 shared instance 与 fan-out

这两个概念必须在 schema 中分开。

### 3.1 shared instance

所有玩家看到并操作同一个 Activity 实例：

```text
一个 ActivityInstanceCm
  Scope = global
  InstanceKey = (definitionId, sessionId, globalKey)
  多个 seat 只是该实例的不同访问者
```

适用于世界级事件、所有玩家共同面对的同一项决策。

特点：

- 只有一个 `ActivityInstanceCm`
- 只有一套实例状态、生命周期和历史记录
- DataPlane 可以向多个 seat 投递同一个 `instanceId`
- `activity.confirm` 必须带 seat 维度，避免无法识别请求来源
- 当前 `ActivityRuntimeService.ResolveOption(Entity, optionId)` 不支持访问者身份，需增加命令侧授权和 seat 上下文

### 3.2 fan-out instance

一次触发为多个作用域各建一份 Activity：

```text
一次触发
  ├─ player 1 → instance A
  ├─ player 2 → instance B
  └─ player 3 → instance C
```

适用于“给每位玩家一份”、“给每个队伍一份”或“按派系分别出现”。

特点：

- 每份实例有自己的 `ScopeHost`
- 准入和去重分别按每个目标作用域计算
- 每个实例可有独立冷却、唯一性和历史
- DataPlane 只把实例投递给对应 owner seat 或对应团队成员

### 3.3 准入键

当前键：

```text
(definitionId, scopeHost.Id)
```

不足之处：

- `Entity.Id` 不是业务稳定身份
- 没有区分 team、representative、faction、global
- 没有 map/session 命名空间
- `Entity.Null` 的 `0` 会把所有全局活动混在一起

建议 v5 的逻辑键为：

```text
(definitionId, scopeDomain, scopeKey)
```

其中：

```text
scopeDomain = representative | team | faction | map_global | application_global
scopeKey    = mapSessionId + logical identity
```

示例：

```text
("supply.overload", "representative", "map-01/player-2")
("war.council", "team", "map-01/team-1")
("world.crisis", "map_global", "session-42/global")
```

实际存储可以继续使用整数注册表 ID，但整数必须由稳定的逻辑键注册得到，不能直接使用 `Entity.Id`。

---

## 4. 面板、命令与呈现

### 4.1 当前 DataPlane 能力

`ActivityWebUiTopicProducer` 已支持：

- 读取 `ActivityRuntimeService.CaptureViews()`
- 按 `ScopeHost` 过滤
- 输出活动、历史和 cue

依据：[`ActivityWebUiTopicProducer.cs`](C:/001_AI/LudotsProd/src/Libraries/Ludots.WebUI.DataPlane/ActivityWebUiTopicProducer.cs)

但当前限制是：

- producer 只有 `ownerScope: Entity`
- `WebUiTopicContext` 只有 `SessionId`、topic、request 参数
- DataPlane session 没有 seatId/playerId 绑定
- 订阅只是“session 是否订阅 topic”，没有 per-seat 授权

依据：

- [`WebUiTopicContext`](C:/001_AI/LudotsProd/src/Libraries/Ludots.WebUI.DataPlane/WebUiDataPlaneRuntime.cs)
- [`WebUiDataPlaneSession`](C:/001_AI/LudotsProd/src/Libraries/Ludots.WebUI.DataPlane/WebUiDataPlaneRuntime.cs)

### 4.2 per-seat topic

建议保持 topic 名称稳定，作用域放在订阅上下文中：

```json
{
  "kind": "subscribe",
  "topic": "panel.activity",
  "payload": {
    "seatId": "seat.0"
  }
}
```

运行时应：

1. 校验 `seatId` 属于当前 DataPlane session；
2. 通过 `ClientLocalSeatRegistry` 解析 seat 的 `playerId`；
3. 根据 Activity 的作用域计算可见实例；
4. 只返回该 seat 有权看到的实例；
5. 对 shared global 实例返回同一个 `instanceId`，而不是复制实例。

不建议让前端自行传 `ownerEntityId` 作为权限依据。实体 ID 是 ECS 地址，不是用户身份。

### 4.3 `activity.confirm`

v4 命令：

```text
activity.confirm {instanceId, optionId}
```

v5 至少应增量为：

```json
{
  "instanceId": 17,
  "optionId": "hold",
  "seatId": "seat.0"
}
```

命令处理顺序：

```text
seatId
  → 当前 DataPlane session 的 seat 绑定
  → playerId
  → representative/team/faction 权限
  → Activity 实例是否允许该 seat 操作
  → ResolveOption
```

当前 showcase handler 只按 `instanceId` 搜索实例并直接调用 `ResolveOption`：  
[`ActivityDispatchShowcaseCommands.cs`](C:/001_AI/LudotsProd/Mods/showcases/activity_dispatch/ActivityDispatchShowcaseMod/ActivityDispatchShowcaseCommands.cs)

这对单人场景可用，但对 shared global 和多人场景不够，必须增加请求来源 seat。

### 4.4 呈现 cue 的投递对象

当前 cue 只有：

```text
Kind, ActivityId, InstanceId, OptionId, Reason, ScopeKey
```

依据：[`ActivityPresentationBuffer.cs`](C:/001_AI/LudotsProd/src/Core/Gameplay/Activities/ActivityPresentationBuffer.cs)

v5 应将“发生了什么”和“发给谁”分开：

```json
{
  "kind": "Presented",
  "activityId": "world.crisis",
  "instanceId": 17,
  "scope": {
    "kind": "global",
    "key": "session-42/global"
  },
  "audience": {
    "kind": "all_seats"
  }
}
```

推荐 audience 类型：

```text
owner_seat
owner_player
team_members
faction_members
all_seats
```

规则：

- per-representative：投递给拥有该 player 的 seat；旁观 seat 是否可见需单独声明
- per-team：投递给该 team 的所有本地 seat
- per-faction：投递给派系成员 seat
- global shared：向所有有权限的 seat 投递同一实例 cue
- global fan-out：每个目标实例按自身作用域投递

核心 Activity buffer 不必为每个 seat 复制一份世界事件；复制应发生在 presentation/DataPlane 路由层。

---

## 5. TriggerGraph 中的作用域表达

### 5.1 当前能力

`OfferActivity` 的签名是：

```text
OfferActivity(activityId, scopeHost)
```

图编译器要求 `source` 输入为 `Entity`，且该 op 仅允许 TriggerGraph：  

- [`GraphOps.cs`](C:/001_AI/LudotsProd/src/Graph/Ludots.Graph.Abstractions/GraphOps.cs)
- [`GraphControlFlowCompiler.Linear.cs`](C:/001_AI/LudotsProd/src/Core/NodeLibraries/GASGraph/GraphControlFlowCompiler.Linear.cs)
- [`GraphOpDescriptorTable.Data.cs`](C:/001_AI/LudotsProd/src/Core/NodeLibraries/GASGraph/GraphOpDescriptorTable.Data.cs)

`LoadPlacedEntity` 只能从地图实体索引读取一个已放置实体：  
[`LoadPlacedEntity.md`](C:/001_AI/LudotsProd/gitbook/reference/graph-node-op-wiki/LoadPlacedEntity.md)

### 5.2 “给一个代表实体”

现有组合足够：

```text
LoadPlacedEntity(player_rep)
  → OfferActivity(source)
```

或：

```text
LoadPlacedEntity(team_rep)
  → OfferActivity(source)
```

这覆盖 per-representative 和 per-team 的单目标派发。

### 5.3 “给全部玩家 / 给某队”

现有 Query 组合不够直接复用：

- `QueryAllMapEntities`、`QueryFilterTeam` 属于 Query 图
- `OfferActivity` 属于 TriggerGraph
- TriggerGraph 不能直接使用 Query 图的 TargetList 查询链
- `TargetListGet` 只能读取已经存在的 target list，不能替代目标收集

依据：[`GraphOpDescriptorTable.Data.cs`](C:/001_AI/LudotsProd/src/Core/NodeLibraries/GASGraph/GraphOpDescriptorTable.Data.cs)、[`GasGraphOpHandlerTable.cs`](C:/001_AI/LudotsProd/src/Core/NodeLibraries/GASGraph/GasGraphOpHandlerTable.cs)

因此以下方案不成立：

```text
TriggerGraph:
  QueryAllMapEntities
  → QueryFilterTeam
  → OfferActivity
```

### 5.4 推荐新增面

建议新增一个显式的 TriggerGraph 目标派发 op，而不是把 Query 图能力偷偷放进 TriggerGraph：

```text
OfferActivityScope
```

概念输入：

```text
activityId
scopeKind: representatives | team | faction | global
scopeKey: 可选的 teamId/factionId
instanceMode: shared | fan_out
```

语义：

- `representatives + fan_out`：遍历地图 Players 绑定，逐玩家 offer
- `team + fan_out`：解析指定 TeamId，对该队伍生成一份
- `all_players + fan_out`：按 Players 绑定逐玩家生成
- `global + shared`：生成一个 map/global 实例
- `global + fan_out`：如果未来需要，按声明的目标集合生成多份

该 op 内部复用：

- `PlayerEntityLookup`
- `TeamEntityLookup`
- `MapSession`
- `ActivityRuntimeService.OfferOrActivateChecked`

不新增 Provider、不新增求值引擎、不新增平行事件总线。

### 5.5 事件轨绑定

现有 TriggerManager 已区分 Map、Entity、Global 事件表：

- `EventScope.Global` 事件通过 `FireGlobalEvent`
- map 事件通过 `FireMapEvent`
- entity-domain mount 当前拒绝 global 事件
- map session 的 global subscriptions 可挂起/恢复

依据：

- [`EventSchema.cs`](C:/001_AI/LudotsProd/src/Core/Scripting/EventSchema.cs)
- [`EventSchemaRegistry.cs`](C:/001_AI/LudotsProd/src/Core/Scripting/EventSchemaRegistry.cs)
- [`TriggerManager.cs`](C:/001_AI/LudotsProd/src/Core/Scripting/TriggerManager.cs)
- [`TriggerGraphMounting.cs`](C:/001_AI/LudotsProd/src/Core/Gameplay/MapTriggers/TriggerGraphMounting.cs)

建议：

- 地图级世界事件：`Map` 或 `Global` 事件进入 map TriggerGraph
- `OfferActivityScope` 决定 Activity 的目标集合
- 不让 Activity schema 再声明 `source_key` 或第二套订阅机制
- `OfferActivity` 继续保留单实体快速路径

---

## 6. v5 schema 增量草案

以下只增加作用域字段，不删除 v4 已有字段。

### 6.1 Activity 定义

```jsonc
{
  "id": "world.supply_crisis",
  "display_name": "补给危机",
  "summary": "世界补给网络进入危急状态。",

  "scope": {
    "kind": "global",
    "instance_mode": "shared",
    "key_domain": "map_global",
    "audience": "all_seats"
  },

  "arrival": "modal",
  "recur": "dedupe",

  "options": [
    {
      "id": "hold",
      "title": "维持现状",
      "body": "接受当前风险。",
      "is_baseline": true,
      "settle": {
        "script": "Graph.Supply.Resolve",
        "args": {
          "verdict": "hold"
        }
      }
    }
  ]
}
```

### 6.2 字段定义

| 字段 | 类型 | 默认值 | 语义 |
|---|---|---|---|
| `scope.kind` | enum | `per_representative` | `per_representative`、`per_team`、`per_faction`、`global` |
| `scope.instance_mode` | enum | `fan_out` | `shared` 或 `fan_out` |
| `scope.key_domain` | enum | 由 kind 推导 | `representative`、`team`、`faction`、`map_global`、`application_global` |
| `scope.audience` | enum/object | 由 kind 推导 | cue 和面板的默认投递对象 |
| `scope.key` | string/object | 空 | 固定 team/faction/global key；代表实体通常由触发目标解析 |
| `scope.require_binding` | bool | `true` | 找不到目标绑定时拒绝加载/派发 |

### 6.3 推荐约束

```text
per_representative:
  instance_mode 只能是 fan_out 或单目标 shared
  scope.key 由 player/representative 绑定解析

per_team:
  scope.key 必须能解析为 TeamId
  team 不存在时 fail-fast

per_faction:
  必须存在 FactionEntityLookup
  没有派系基础设施时拒绝加载

global:
  shared 使用一个 session/global key
  fan_out 必须声明额外目标解析规则
```

### 6.4 运行时实例记录

建议将现有 `ActivityInstanceCm` 增量扩展为逻辑作用域字段：

```text
ActivityInstanceCm
  DefinitionId
  InstanceId
  State
  ScopeHost
  ScopeDomain
  ScopeKeyId
  InstanceMode
  SelectedOptionIndex
  Revision
  DispatchTick
```

其中：

- `ScopeHost` 仍作为图执行的实体上下文
- `ScopeDomain + ScopeKeyId` 作为业务去重和准入键
- global shared 可以使用 `Entity.Null` 作为执行 host，但不能以 `0` 代替完整作用域键

---

## 7. 作用域侧待裁定点

1. `global` 的边界是单个 `MapSession`，还是整个应用/对局实例。
2. `MapSession` 是否需要稳定的 session UUID，以支持同一地图多实例并存。
3. `per_faction` 是否正式建立独立 Faction 实体模型，还是明确声明 faction 等同 team。
4. shared global 实例是否允许所有 seat 看到完整状态，还是只看到各自授权投影。
5. per-team 活动在同队多 seat 时，是显示一份共享活动，还是每个 seat 各有本地确认入口。
6. seat 是否允许旁观其他玩家的 per-representative 活动。
7. Activity 实例的准入键是否持久化为字符串逻辑键，还是继续使用整数注册表 ID。
8. entity 被销毁或代表关系变化后，Activity 的作用域是否冻结在原逻辑身份，还是转移到新代表实体。
9. global shared 活动的 cue 是核心层记录一次再由 presentation fan-out，还是核心层直接生成 audience 列表。
10. `activity.confirm` 是否始终要求 `seatId`，包括单机单 seat 场景。

---

## 8. 引擎缺口清单

| 能力 | 现状 | 处理建议 |
|---|---|---|
| Player → representative 解析 | 已有 `PlayerEntityLookup` | 复用 |
| Team → representative 解析 | 已有 `TeamEntityLookup` | 复用 |
| seat → player → representative | `MapLaunchContext`、`ClientLocalSeatRegistry` 已有 | 复用并接入 Activity 授权 |
| MapSession 管理 | 已有 `MapSessionManager`、`MapSession` | 复用；可能需新增稳定 session key |
| per-representative Activity | `ScopeHost` 已支持 | 增加逻辑 scope key |
| per-team Activity | Team lookup 已支持，Activity 未直接绑定 | 新增 scope resolver |
| per-faction Activity | 无 Core 级派系 lookup/身份合同 | 新增，或明确禁止该 kind |
| global shared Activity | `Entity.Null` 可作占位，但无 global key | 新增 global scope key/runtime 语义 |
| fan-out 实例生成 | `OfferActivity` 只支持单实体 | 新增显式 fan-out 派发 op/service 方法 |
| TriggerGraph 查询全部玩家 | Query op 只在 Query 图可用 | 不跨图复用；由新 scope op 封装 |
| TriggerGraph 按队伍派发 | `OfferActivity` 无 team selector | 新增 `OfferActivityScope` 或等价正式 op |
| DataPlane per-seat 订阅 | 只有 session/topic 订阅 | 增加 seat 绑定和订阅参数校验 |
| Activity topic owner 过滤 | 已有 entity owner 过滤 | 扩展为 scope resolver + seat audience |
| `activity.confirm` seat 维度 | 当前只有 instanceId/optionId | 新增 `seatId`，并做权限校验 |
| cue 投递对象 | cue 只有 `ScopeKey` | 增加 audience/recipient 路由信息 |
| 全局实例历史 | 当前历史按 ActivityView 输出，未区分 global key | 增加 scope domain/key |
| 存档 | Activity snapshot 只有 `NextInstanceId` 和 signal IDs | 增加实例作用域键及 global session key |
| 事件轨 | Map/Global 事件表和 schema 已有 | 复用；由 TriggerGraph scope op 决定 Activity 目标 |
| AgentBridge seat 验证 | 本研究范围内未发现 Activity 专用 seat 钩子 | 新增验证入口，至少能按 seat 查询、确认和检查 cue |

---

## 9. 场景

### 场景一：玩家自己的王廷决议

```gherkin
Feature: 玩家代表实体的活动作用域
  Scenario: 活动只属于一个玩家
    Given 地图 Players 中 playerId 为 1 的代表实体已经绑定
    And seat.0 当前占有 playerId 为 1
    When TriggerGraph 向该代表实体 OfferActivity
    Then 只创建一个 per-representative 活动实例
    And 活动的 ScopeKey 指向 playerId 1
    And seat.0 能在活动面板看到该实例
    And 其他玩家的 seat 看不到该实例
```

### 场景二：队伍共同决议

```gherkin
Feature: 队伍活动作用域
  Scenario: 同一队伍共享一份活动实例
    Given teamId 为 1 的队伍代表实体已经绑定
    And playerId 为 1 和 playerId 为 2 都属于 teamId 1
    When TriggerGraph 向 teamId 1 派发活动
    Then 只创建一份 per-team 活动实例
    And teamId 1 的本地 seat 都能收到该实例的呈现 cue
    And 活动实例的 ScopeKey 不随某个具体 seat 改变
```

### 场景三：全局 shared 事件

```gherkin
Feature: 全局共享活动
  Scenario: 所有玩家看到同一个世界事件
    Given 地图已经有三个本地 seat
    When 世界级 TriggerGraph 派发一个 global shared 活动
    Then 只创建一个活动实例
    And 三个 seat 收到同一个 instanceId
    And activity.confirm 请求必须带 seatId
    And 引擎能根据 seatId 判断该 seat 是否有权确认
```

### 场景四：全体玩家 fan-out

```gherkin
Feature: 全体玩家活动分发
  Scenario: 每个玩家得到自己的活动副本
    Given 地图 Players 中存在三个玩家代表实体
    When TriggerGraph 执行 fan-out 派发
    Then 创建三个 per-representative 活动实例
    And 每个实例拥有不同的 ScopeKey
    And 每个 seat 只收到自己的实例
    And 一个玩家确认选项不会改变其他玩家的实例状态
```

---

## 10. UAT

```gherkin
Feature: Activity 作用域与引擎绑定

  Scenario: 代表实体作用域沿用现有玩家绑定
    Given 地图 Players 已声明 PlayerId 和 RepresentativeInstanceId
    And 本次进图的 LocalSeats 已声明 seatId 和 PlayerId
    When 活动被派发给该玩家代表实体
    Then 活动归属该玩家而不是归属当前 seat
    And seat 只作为输入和命令来源

  Scenario: 全局 shared 活动不被错误复制
    Given 活动声明 scope.kind 为 global
    And 活动声明 scope.instance_mode 为 shared
    When TriggerGraph 触发该活动
    Then 全局只存在一个 ActivityInstanceCm
    And 所有有权限的 seat 收到同一个 instanceId

  Scenario: fan-out 活动按目标作用域分别去重
    Given 活动声明 instance_mode 为 fan_out
    And 两个玩家都属于同一张地图
    When TriggerGraph 对全部玩家执行一次派发
    Then 每个玩家最多得到一份自己的活动实例
    And 一个玩家的 pendingDedupe 不阻止另一个玩家获得实例

  Scenario: 确认命令带有 seat 维度
    Given seat.0 和 seat.1 都连接到同一 DataPlane
    And global shared 活动已经展示
    When seat.0 发送 activity.confirm
    Then 命令处理器能识别请求来自 seat.0
    And 引擎按 seat.0 的玩家身份执行权限检查
    And 不接受缺失 seatId 的多人确认请求

  Scenario: TriggerGraph 不越过图种边界
    Given TriggerGraph 需要向某个队伍派发活动
    When 图尝试直接使用 QueryAllMapEntities 或 QueryFilterTeam
    Then 编译器拒绝该图
    And 作者必须使用正式的作用域派发 op
    And 引擎不会静默把 Query 图能力注入 TriggerGraph

  Scenario: 未建模的派系作用域快速失败
    Given 活动声明 scope.kind 为 per_faction
    And 当前地图没有 FactionEntityLookup
    When 引擎加载活动定义
    Then 加载失败并指出缺少派系绑定
    And 活动不会静默降级为 per-team
```

---

## 结论

v5 的核心不是把 Activity 再做一套玩家系统，而是把现有实体 host 扩展为稳定的作用域合同：

```text
业务作用域 = scopeDomain + scopeKey
执行上下文 = ScopeHost entity
输入来源   = seat
呈现投递   = audience
```

推荐保留现有 `OfferActivity` 作为单实体快速路径，并新增一个正式的作用域派发入口处理“全部玩家、某队、全局 shared/fan-out”。这样可以复用现有 `MapSession`、玩家/队伍 lookup、TriggerManager、ActivityRuntimeService 和 DataPlane 管线，不引入第二套查询或事件系统。
