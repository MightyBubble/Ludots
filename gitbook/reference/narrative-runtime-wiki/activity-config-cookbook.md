# Activity / Task 配置手册

状态：设计稿。这里把常见场景压成可抄的配置形状，方便作者先写内容再回头接 Graph。本文中的 v5 字段仍需等实现落地后才能直接使用。

## 1. 概述

这份手册只做一件事：把常见场景拆成 Activity、Task、Effect 和 Graph 的组合样子。它不解释原理，不讲抽象分层，只保留作者真正会抄的形状。

四个判断先记住：

1. 现在要不要玩家拍板？要，就用 Activity。
2. 这件事是不是要跨周期追踪？是，就用 Task。
3. 要不要给某个状态挂时长或冷却？要，就用 Effect。
4. 规则是读出来的，还是写进去的？读用 Validation，写用 Script 或 TriggerGraph。

当前代码里，Activity 和 Task 仍是旧字段体系，见 [`src/Core/Gameplay/Activities/ActivityDefinitions.cs`](../../../src/Core/Gameplay/Activities/ActivityDefinitions.cs) 和 [`src/Core/Gameplay/Tasks/TaskDefinitions.cs`](../../../src/Core/Gameplay/Tasks/TaskDefinitions.cs)。这份手册写的是目标态，不是今天的现状。

## 2. 结构

### 2.1 文件总览

```text
assets/
  Activities/activities.json
  Tasks/tasks.json
  GAS/effects.json
  GAS/graphs.json
  Rng/distributions.json
  Events/custom_events.json
  Maps/your_map.json
  config_catalog.json
```

### 2.2 Activity 字段

| 字段 | 意思 |
|---|---|
| `id` | 唯一标识 |
| `display_name` | 弹层标题 |
| `summary` | 弹层正文 |
| `picture` | 视觉资产 |
| `scope.kind` | `per_player` / `per_team` / `per_faction` / `global` |
| `scope.instance_mode` | `fan_out` 或 `shared` |
| `scope.audience` | 投递受众 |
| `arrival` | `modal` / `notice` / `auto` / `{ pool }` |
| `gate.blocked_by_tags` | 不能出现的标签 |
| `gate.required_tags` | 必须存在的标签 |
| `when.graph` | 条件 Graph |
| `options[].show_when.graph` | 选项是否可见 |
| `options[].enable_when.graph` | 选项是否可操作 |
| `options[].settle.script` | 选项结算 Graph |
| `settle.script` | auto 活动结算 Graph |
| `immediate.script` | 弹层前 Graph |
| `after.script` | 结算后 Graph |
| `timeout.ticks` | 弹层后多久超时 |
| `timeout.on_timeout` | 超时默认选项 |
| `resolve.vote.*` | 多人投票合同 |
| `urgency` | 宿主时间压力提示 |

### 2.3 Task 字段

| 字段 | 意思 |
|---|---|
| `id` | 唯一标识 |
| `display_name` | 追踪列表标题 |
| `summary` | 追踪列表描述 |
| `objectives[].id` | 目标行 id |
| `objectives[].title` | 目标行文字 |
| `timeout.ticks` | 机械翻态时长 |
| `timeout.to_state` | 到期进入的状态 |

Task 没有 `completion_rule`、`signal_key` 或 `condition_key` 的设计理由，是把行为从对象里拿掉，交给 Graph。当前实现还没有完成这一步，所以旧字段仍在代码里。

## 3. 详情

下面的 13 个案例按“玩家会怎么用”排。

### 案例 1：随机事件弹层

```json
{
  "id": "flavor.wandering_merchant",
  "display_name": "流浪商人",
  "summary": "一位商人拦住了你，兜售来自东方的奇珍。",
  "picture": "merchant_encounter",
  "gate": { "blocked_by_tags": ["cd.merchant"] },
  "options": [
    {
      "id": "buy",
      "title": "花 100 金买下",
      "enable_when": { "graph": "Graph.Gate.HasGold", "args": { "amount": 100 } },
      "settle": { "script": "Graph.Merchant.Buy" }
    },
    {
      "id": "pass",
      "title": "婉拒",
      "settle": { "script": "Graph.Merchant.Pass" }
    }
  ],
  "timeout": { "ticks": 600 }
}
```

配套冷却：

```json
[
  {
    "id": "Effect.Merchant.CoolDown",
    "duration": { "durationTicks": 480, "clockId": "Step" },
    "grantedTags": [{ "tag": "cd.merchant" }]
  }
]
```

发射图只负责把活动送出来，不负责解释活动是什么。

### 案例 2：候选池

```json
[
  {
    "id": "pool.quarterly",
    "stream": "quarterly_stream",
    "streamSeed": 20260101,
    "entries": [
      { "id": "flavor.wandering_merchant", "weight": 30 },
      { "id": "flavor.good_harvest", "weight": 25 },
      { "id": "flavor.bandit_report", "weight": 20 },
      { "id": "flavor.strange_omen", "weight": 15 },
      { "id": "flavor.old_friend", "weight": 10 }
    ]
  }
]
```

### 案例 3：全局通报

```json
{
  "id": "news.war_declared",
  "display_name": "战争爆发",
  "summary": "A 国对 B 国宣战。",
  "picture": "war_declaration",
  "scope": { "kind": "global" },
  "arrival": "notice",
  "settle": { "script": "Graph.News.WarSettle" }
}
```

### 案例 4：外交协议

Task 本体：

```json
{
  "id": "treaty.truce",
  "display_name": "停战条约",
  "objectives": [{ "id": "hold", "title": "维持和平。" }],
  "timeout": { "ticks": 1825000, "to_state": "failed" }
}
```

Activity 拍板：

```json
{
  "id": "treaty.sign",
  "display_name": "停战提议",
  "summary": "对方提出停战 5 年。",
  "options": [
    { "id": "accept", "title": "接受", "settle": { "script": "Graph.Treaty.Accept" } },
    { "id": "reject", "title": "拒绝", "settle": { "script": "Graph.Treaty.Reject" } }
  ],
  "timeout": { "ticks": 1200, "on_timeout": "option:reject" }
}
```

配套标签：

```json
[
  {
    "id": "Effect.Treaty.NoWar",
    "duration": { "durationTicks": 1825000, "clockId": "Step" },
    "grantedTags": [{ "tag": "status.truce" }]
  }
]
```

### 案例 5：击杀 N 只怪

Task：

```json
{
  "id": "quest.kill_murlocs",
  "display_name": "清剿鱼人",
  "objectives": [{ "id": "kill", "title": "击杀鱼人 x3" }]
}
```

TriggerGraph：

```jsonc
{
  "id": "Graph.Quest.MurlocWatch",
  "kind": "TriggerGraph",
  "entries": [{ "label": "on_kill", "event": "Unit.Died", "start": "victim", "refire": "restart" }],
  "nodes": [
    { "id": "victim", "op": "LoadEntryPayloadEntity", "payloadKey": "victim" },
    { "id": "is_murloc", "op": "HasTag", "tag": "Creature.Murloc" },
    { "id": "check", "op": "JumpIfFalse" },
    { "id": "count", "op": "ReadMapVarInt", "var": "quest.murloc_kills" },
    { "id": "one", "op": "ConstInt", "intValue": 1 },
    { "id": "add", "op": "AddInt" },
    { "id": "write", "op": "WriteMapVarInt", "var": "quest.murloc_kills" },
    { "id": "check3", "op": "CompareEqInt" },
    { "id": "branch", "op": "JumpIfFalse" },
    { "id": "complete", "op": "CompleteTask", "taskId": "quest.kill_murlocs" },
    { "id": "offer", "op": "OfferActivity", "activityId": "quest.murlocs_done" },
    { "id": "done", "op": "HaltReturnInt" }
  ]
}
```

### 案例 6：每日签到

```json
[
  {
    "id": "Effect.Daily.LoginCD",
    "duration": { "durationTicks": 86400, "clockId": "Step" },
    "grantedTags": [{ "tag": "cd.daily_login" }]
  }
]
```

```json
{
  "id": "daily.login",
  "display_name": "每日签到",
  "gate": { "blocked_by_tags": ["cd.daily_login"] },
  "options": [
    { "id": "claim", "title": "签到", "settle": { "script": "Graph.Daily.Claim" } }
  ]
}
```

```jsonc
{
  "id": "Graph.Daily.Emit",
  "kind": "TriggerGraph",
  "entries": [{ "label": "on_login", "event": "Player.LoggedIn", "start": "scope", "once": true }],
  "nodes": [
    { "id": "scope", "op": "LoadCaster" },
    { "id": "offer", "op": "OfferActivity", "activityId": "daily.login" },
    { "id": "done", "op": "HaltReturnInt" }
  ]
}
```

### 案例 7：投票决议

```json
{
  "id": "council.declare_war",
  "display_name": "战争决议",
  "scope": { "kind": "global" },
  "options": [
    { "id": "war", "title": "宣战", "settle": { "script": "Graph.Council.War" } },
    { "id": "peace", "title": "维持和平", "settle": { "script": "Graph.Council.Peace" } }
  ],
  "timeout": { "ticks": 3600 },
  "resolve": {
    "vote": {
      "voters": { "kind": "players" },
      "rule": "weighted_majority",
      "weight": { "source": "attribute", "attribute_key": "Diplomacy.Clout" },
      "quorum": { "mode": "percent", "value": 50 },
      "settle_when": "rule_met",
      "timeout": { "ticks": 3600, "on_timeout": "count" }
    }
  }
}
```

### 案例 8：危机进度

```json
{
  "id": "crisis.civil_war",
  "display_name": "内战危机",
  "objectives": [{ "id": "survive", "title": "维持稳定。" }]
}
```

```jsonc
{
  "id": "Graph.Crisis.Watch",
  "kind": "TriggerGraph",
  "entries": [{ "label": "monthly", "event": "Calendar.MonthAdvanced", "start": "scope", "refire": "restart" }],
  "nodes": [
    { "id": "scope", "op": "LoadCaster" },
    { "id": "unrest", "op": "LoadSelfAttribute", "attribute": "Unrest" },
    { "id": "thresh", "op": "ConstFloat", "floatValue": 100 },
    { "id": "cmp", "op": "CompareGtFloat" },
    { "id": "branch", "op": "JumpIfFalse" },
    { "id": "fail", "op": "FailTask", "taskId": "crisis.civil_war" },
    { "id": "offer", "op": "OfferActivity", "activityId": "crisis.civil_war_break" },
    { "id": "done", "op": "HaltReturnInt" }
  ]
}
```

### 案例 9：国策树

```json
{
  "id": "focus.industry_1",
  "display_name": "工业建设 I",
  "objectives": [{ "id": "wait", "title": "推进中（70 天）" }],
  "timeout": { "ticks": 70000, "to_state": "failed" }
}
```

```json
{
  "id": "focus.industry_2",
  "display_name": "工业建设 II",
  "gate": { "required_tags": ["focus.industry_1.done"] },
  "options": [
    { "id": "start", "title": "开始推进", "settle": { "script": "Graph.Focus.StartIndustry2" } }
  ]
}
```

### 案例 10：副本锁定

```json
[
  {
    "id": "Effect.Lockout.Weekly",
    "duration": { "durationTicks": 604800, "clockId": "Step" },
    "grantedTags": [{ "tag": "lockout.dungeon_abyss" }]
  }
]
```

```json
{
  "id": "dungeon.abyss_enter",
  "gate": { "blocked_by_tags": ["lockout.dungeon_abyss"] },
  "options": [
    { "id": "enter", "title": "进入", "settle": { "script": "Graph.Dungeon.Enter" } },
    { "id": "leave", "title": "暂不", "settle": { "script": "Graph.Dungeon.NotYet" } }
  ]
}
```

### 案例 11：双方同意交易

```json
{
  "id": "trade.offer",
  "display_name": "交易提议",
  "options": [
    { "id": "accept", "title": "同意", "settle": { "script": "Graph.Trade.Accept" } },
    { "id": "decline", "title": "拒绝", "settle": { "script": "Graph.Trade.Decline" } }
  ],
  "timeout": { "ticks": 600, "on_timeout": "option:decline" },
  "resolve": {
    "vote": {
      "voters": { "kind": "entities", "entity_ids": ["trader_a", "trader_b"] },
      "rule": "unanimous",
      "settle_when": "all_voted",
      "timeout": { "ticks": 600, "on_timeout": "option:decline" }
    }
  }
}
```

### 案例 12：条件选项

```json
{
  "id": "court.marriage",
  "options": [
    { "id": "accept", "title": "接受联姻", "settle": { "script": "Graph.Court.Marry" } },
    { "id": "demand_dowry", "title": "要求嫁妆", "enable_when": { "graph": "Graph.Gate.HasLeverage" }, "settle": { "script": "Graph.Court.MarryWithDowry" } },
    { "id": "refuse", "title": "婉拒", "settle": { "script": "Graph.Court.Refuse" } },
    { "id": "alliance", "title": "以联姻缔结同盟", "show_when": { "graph": "Graph.Gate.HasAlliance" }, "settle": { "script": "Graph.Court.Alliance" } }
  ]
}
```

### 案例 13：时间压力决策

```json
{
  "id": "combat.dodge_roll",
  "display_name": "闪避！",
  "summary": "一支箭正飞向你。",
  "urgency": "bullet",
  "timeout": { "ticks": 180, "clock": "fixed_frame", "on_timeout": "option:hit" },
  "options": [
    { "id": "dodge_left", "title": "左闪", "settle": { "script": "Graph.Combat.DodgeLeft" } },
    { "id": "dodge_right", "title": "右闪", "settle": { "script": "Graph.Combat.DodgeRight" } },
    { "id": "hit", "title": "被击中", "settle": { "script": "Graph.Combat.TakeHit" } }
  ]
}
```

### 2.4 核心规则

- `accept` 和 `refuse` 这类无 `enable_when` 的选项要天然可达。
- `show_when` 决定显示与否，`enable_when` 决定能不能点。
- `Task` 没有 `complete` / `fail` 条件字段，完成和失败由 Graph 决定。
- `global` 活动不需要额外复制成每个玩家一份，除非明确要求 fan-out。
- `urgency` 只描述宿主态势，不是活动自己的时钟实现。

## 4. 场景

- 随机事件弹层：商人、征兆、一次性招募。
- 持续追踪：击杀任务、护送任务、国策树、危机进度。
- 协议签订：停战、盟约、交易。
- 全局通报：战争爆发、灾害到来、系统公告。
- 时间压力：闪避、限时抉择、子弹时间。

## 5. 边界

- 这份手册不定义当前运行时是否已经支持这些字段。
- 这份手册不替代 Activity v5 设计正文。
- 这份手册不把 Task 改写成 Activity，也不把 Activity 的一次拍板改写成持续目标。
- 任何没有来源的默认值、恢复时间和容错策略都不能在这里补出来。
- 当前代码还保留 `completion_rule`、`signal_key`、`repeat_policy`、`dispatch_policy` 等旧字段；它们是现实现状，不是这份手册的理想形状。

## 6. UAT

```gherkin
Feature: 作者按场景抄配置

  Scenario: 随机事件使用 Activity
    Given 一个场景只要求玩家选一次
    When 作者创建该场景的配置
    Then 作者使用 Activity
    And 结算只通过一个 Script Graph 完成

  Scenario: 持续追踪使用 Task
    Given 一个场景需要跨周期记录进度
    When 作者创建该场景的配置
    Then 作者使用 Task
    And 进度由 Graph 更新而不是由 Task 自己计算

  Scenario: 冷却用标签而不是字段
    Given 一个场景需要 8 天游离冷却
    When 作者写配置
    Then 作者使用 Effect 授予冷却标签
    And 不在 Activity 定义里写独立冷却逻辑

  Scenario: 条件选项分成可见和可操作两层
    Given 一个 Activity 有三个选项
    And 其中一个选项只允许高金币玩家操作
    When 玩家打开面板
    Then 该选项可以保持可见
    And 只有满足条件时才可点击

  Scenario: 需要投票的活动显式写 vote 块
    Given 一个活动需要多人表决
    When 作者写配置
    Then 作者在 Activity 里写 resolve.vote
    And 不把投票条件塞进某个选项内部
```
