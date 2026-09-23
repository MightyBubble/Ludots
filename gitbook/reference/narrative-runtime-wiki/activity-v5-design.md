# Activity v5 设计：图化决策活动

状态：设计稿，尚未替换当前 Activity 运行时合同。本文是 #1394 的正式设计入口；实现进度以代码和测试为准。

## 1. 概述

Activity 表示一个短生命周期的决策时刻：玩家看到事件，选择一个选项，系统完成一次结算。它不承载跨周期进度；需要长期追踪的内容交给 Task，需要多页对白或时间轴的内容交给 Dialogue / Sequencer。

v5 固定三条不变量：

1. 数据由物化 entity 持有。
2. 行为只存在于 Graph。
3. 声明字段不拥有一套私有 C# 求值器。

这三条规则解决两个实际问题：

- 活动面板、存档和图执行读取同一份实例状态，不再各自维护副本。
- 作者可以用配置表描述常见活动，用 Graph 表达条件、发射和结算；新增玩法靠数据和图的组合，不靠给定义对象不断加解释字段。

当前代码仍是旧版 Provider 路径，见 [`src/Core/Gameplay/Activities/ActivityDefinitions.cs`](../../../src/Core/Gameplay/Activities/ActivityDefinitions.cs)、[`src/Core/Gameplay/Activities/ActivityRuntimeService.cs`](../../../src/Core/Gameplay/Activities/ActivityRuntimeService.cs) 和 [`src/Tests/GasTests/Integration/ActivityRuntimeTests.cs`](../../../src/Tests/GasTests/Integration/ActivityRuntimeTests.cs)。本文中的 v5 字段和 Graph 节点在实现前不能直接当作现行配置使用。

## 2. 结构

### 2.1 内容边界

| 需要回答的问题 | 内容类型 |
|---|---|
| 玩家现在必须选一个，选完就结束？ | Activity |
| 要跨周期追踪进度、阶段、完成或失败？ | Task |
| 要多页对白或演出时间轴？ | Dialogue / Sequencer |
| 选项后果需要长期存在？ | Activity 结算中创建 Task，或写入属性、标签、Record |

外交协议的本体是 Task；签约、修约和撕约是 Activity，结算结果回写协议状态。

### 2.2 三层作者模型

| 层 | 作者写什么 | 运行时使用什么 |
|---|---|---|
| 声明层 | 活动定义、发射时刻表、条件监视表 | 引擎提供的通用 Graph 读取这些表并组合通用节点 |
| 定制层 | TriggerGraph、Validation、Script | Graph 本身就是行为 |
| 运行时层 | 实例数据 | entity 上的组件和可持久化状态 |

声明字段只描述数据或机械状态。条件、发射、结算等行为必须通过 Graph 表达，不能在 `ActivityDefinition` 内新增一套隐式求值逻辑。

### 2.3 四个互相独立的概念

```text
业务作用域 = scope.kind + scope key
执行上下文 = ScopeHost entity
输入来源   = seat
呈现投递   = audience
```

- `scope.kind` 描述活动属于哪个参与者：`per_player`、`per_team`、`per_faction`、`global`。
- `ScopeHost` 是 Graph 的 `Caster`，代表当前参与者；它不是玩家身份本身。
- `seat` 只决定谁发起操作和是否有权限操作，不能替代参与者身份。
- `audience` 决定 cue 和面板投递给谁。
- 前端不得把 ECS entity id 当作玩家身份传回运行时。
- Core 尚无 faction 代表实体时，`per_faction` 必须在加载期拒装，不能静默改成 team。

### 2.4 运行时实例

每个未结算活动是一枚带 `ActivityInstanceCm` 的 entity。v5 需要至少保存：

```text
DefinitionId
InstanceId
State
ScopeHost
SelectedOptionIndex
Revision
Scope snapshot
Named context bindings
Ballot data（投票活动）
```

实例状态只有：

```text
pending -> active -> resolved
```

`resolved` 之后只作为历史数据保留，活动查询不再返回。面板和叙事层只读投影，不另存一份活动真相。

## 3. 详情

### 3.1 配置文件布局

```text
assets/config_catalog.json           声明要加载的配置文件
assets/Activities/activities.json    Activity 定义
assets/GAS/graphs.json               Validation / Script / TriggerGraph
assets/Rng/distributions.json        候选池权重
assets/Events/custom_events.json     自定义事件
assets/Tasks/tasks.json              Activity 结算接棒的 Task
assets/Maps/<map>.json               作用域实体和地图节拍
Assets/PanelKit/panel_manifest.json  面板绑定
```

### 3.2 Activity 定义

下面是 v5 的完整形状。它是设计合同，不代表当前 loader 已支持所有字段。

```jsonc
{
  "id": "court.marriage_proposal",
  "display_name": "联姻提议",
  "summary": "邻国提议联姻，接受或婉拒。",
  "picture": "court_marriage",

  "scope": {
    "kind": "per_player",
    "instance_mode": "fan_out",
    "audience": "owner_seat"
  },

  "arrival": "modal",
  "gate": {
    "blocked_by_tags": ["cd.court"],
    "required_tags": []
  },
  "recur": "dedupe",

  "when": {
    "graph": "Graph.Gate.CourtRep",
    "args": { "threshold": 80 }
  },
  "immediate": {
    "script": "Graph.Court.PrepPortraits"
  },

  "options": [
    {
      "id": "accept",
      "title": "接受联姻",
      "body": "两姓之好，就此缔结。",
      "show_when": { "graph": "Graph.Gate.NotKin" },
      "enable_when": {
        "graph": "Graph.Gate.CanDowry",
        "args": { "gold": 100 }
      },
      "settle": {
        "script": "Graph.Court.MarrySettle",
        "args": { "prestige": 5 }
      }
    },
    {
      "id": "decline",
      "title": "婉拒",
      "settle": { "script": "Graph.Court.DeclineSettle" }
    }
  ],

  "after": {
    "script": "Graph.Court.CleanupFlags"
  },

  "resolve": {
    "vote": {
      "voters": { "kind": "players" },
      "rule": "weighted_majority",
      "weight": {
        "source": "attribute",
        "attribute_key": "diplomacy.clout",
        "snapshot_at": "open"
      },
      "quorum": { "mode": "percent", "value": 50 },
      "secret": false,
      "allow_change": true,
      "abstain": "allowed",
      "tie_break": "baseline",
      "settle_when": "rule_met",
      "timeout": {
        "ticks": 600,
        "clock": "step",
        "on_timeout": "count"
      }
    }
  },

  "urgency": "normal"
}
```

字段语义：

- `arrival`：`modal` 要求拍板，`notice` 先入列表，`auto` 直接执行根 `settle`。候选池用 `{ "pool": "pool.id" }`。
- `gate`：只做标签交集检查。冷却、一次性和限次不是 Activity 字段，而是效果与 Graph 组合出的标签或计数。
- `recur`：`dedupe` 防止同一作用域挂起同名活动重复出现，`always` 允许重复实例。
- `when`、`show_when`、`enable_when`：分别控制活动、选项是否出现，以及选项是否可操作；它们都调用 Validation Graph。
- `immediate`：面板出现前运行的 Script Graph。
- `settle`：选项结算使用的 Script Graph；`auto` 活动使用根 `settle`。
- `after`：选项结算之后运行一次的 Script Graph。
- `resolve.vote`：存在时进入投票；没有时就是单决策者活动。
- `urgency`：`normal` 正常运行，`pause` 请求宿主暂停，`bullet` 请求宿主减速。联机时不暂停全局模拟，宿主应忽略这一提示。

### 3.3 标签门和再入

v5 不再把 `cooldown`、`once`、`max_times` 设计成 Activity 定义字段：

- 冷却：结算或发射图施加带期限的效果和标签。
- 一次性：结算图施加永久完成标签。
- 限次：结算图更新 TagCount 或其他已登记的计数数据。
- 互斥：只有在明确解决“实例结束时撤销标签”之前，才能作为单独的机械能力进入实现；不能用未定义的默认行为代替。

这样做的目的，是让标签和属性仍然经过现有 GAS 效果管线，而不是由 Activity 自己偷偷写世界状态。

### 3.4 作用域快照和参数

Graph 执行时：

```text
Caster        = ScopeHost
ExplicitTarget = 空
```

发射图可以把事件中的实体保存为命名作用域，例如 `enemy_army`。结算图按名称读取快照；活动实例结算并销毁后，快照随实例清理。

配置中的标量 `args` 进入 Graph 的 EntryPayload。Graph 通过 `LoadEntryPayloadInt`、`LoadEntryPayloadFloat` 等节点读取。加载期必须校验参数键和 Graph 可达性；未知 Graph、未知键和缺少必需参数都要让整包拒装，并在错误中带出键名。

### 3.5 Graph 类型和边界

| Graph 类型 | 负责 | 明确不负责 |
|---|---|---|
| Validation | 读取属性、标签、任务和 Record，比较并返回 bool | 任何写操作 |
| Script | 结算、面板前置、结算后清理、创建 Task、写 Record | 隐藏的等待、直接绕过效果管线写 GAS 状态 |
| TriggerGraph | 订阅事件、节拍、条件监视、发射 Activity、旅程侧写 | 把查询链塞进 Activity 对象内部 |

现有复用入口：

- Graph 指令枚举：[`src/Graph/Ludots.Graph.Abstractions/GraphOps.cs`](../../../src/Graph/Ludots.Graph.Abstractions/GraphOps.cs)
- Graph API：[`src/Core/NodeLibraries/GASGraph/IGraphRuntimeApi.cs`](../../../src/Core/NodeLibraries/GASGraph/IGraphRuntimeApi.cs)
- Handler 注册与执行：[`src/Core/NodeLibraries/GASGraph/GasGraphOpHandlerTable.cs`](../../../src/Core/NodeLibraries/GASGraph/GasGraphOpHandlerTable.cs)
- Activity 入口：[`src/Core/NodeLibraries/GASGraph/Host/GasGraphRuntimeApi.cs`](../../../src/Core/NodeLibraries/GASGraph/Host/GasGraphRuntimeApi.cs)

实现 v5 时继续沿用这些入口，不新增 Activity 专用解释器、事件总线或面板管线。

### 3.6 发射

手写 TriggerGraph 的最小形状：

```jsonc
{
  "id": "Graph.Rhythm.Daily",
  "kind": "TriggerGraph",
  "entries": [
    {
      "label": "on_day",
      "event": "Calendar.DayAdvanced",
      "start": "scope",
      "refire": "restart"
    }
  ],
  "nodes": [
    { "id": "scope", "op": "LoadPlacedEntity", "instanceId": "council" },
    { "id": "offer", "op": "OfferActivity", "activityId": "court.marriage_proposal" },
    { "id": "done", "op": "HaltReturnInt" }
  ],
  "controlEdges": [],
  "valueEdges": []
}
```

声明层可以再提供一张通用发射解释图：作者只填写活动 id、节拍、机会门、权重和作用域，通用图负责组合 `LoadConfig`、`Compare`、`WeightedPick` 和 `OfferActivity`。表格装不下的逻辑才写定制 TriggerGraph。

延迟和超时分两段：

```text
触发 -> offer delay -> 入列或弹层 -> popup timeout -> 自动选项
```

- `offer delay` 是发射图或 `OfferActivity` 的参数。
- `notice` 活动入列时不开始弹层倒计时。
- `popup timeout` 从玩家能看到选项时开始。
- `timeout.clock = step` 跟随模拟步，`fixed_frame` 跟随实时帧。

### 3.7 Task 接口

Activity 只负责一次决策。需要长期追踪的结果由 Graph 创建 Task：

```text
领域事件
 -> 读取事件实体和条件
 -> 更新任务展示状态或进度数据
 -> CompleteTask / FailTask
 -> OfferActivity（如需要下一次决策）
```

Task 目标行是展示数据，行为仍然来自 Graph。当前 Task 代码还存在 Provider 条件和信号求值，迁移边界见 [`task.md`](task.md)。

## 4. 场景

### 4.1 玩家遇到随机事件

周期 TriggerGraph 触发候选池，选出一个 Activity。Activity 显示两个选项；购买选项由 Validation Graph 检查金币，拒绝选项作为无条件可达的兜底。

### 4.2 全局通报

Activity 使用 `scope.kind = global` 和 `arrival = notice`。它只把同一条通知投递给受众，不要求每个玩家创建一份副本，也不要求玩家提交选项。

### 4.3 任务完成后领取奖励

击杀事件进入 TriggerGraph。Graph 读取事件中的目标实体，更新任务进度；达到阈值后完成 Task，再发一个领取奖励的 Activity。选项结算不能直接再开一层 Activity。

### 4.4 多人投票

一个 shared Activity 绑定多个投票者。开票时冻结权重，按 `rule`、`quorum`、`tie_break` 和超时策略计算结果。投票输入来源是 seat，但活动归属仍由 `scope` 决定。

### 4.5 时间压力

`urgency = pause` 配合 `timeout.clock = step` 时，宿主暂停模拟和倒计时；配合 `fixed_frame` 时，面板虽然暂停世界，倒计时仍继续。`urgency = bullet` 只作为宿主减速提示，不能让 Activity 自己修改时钟。

## 5. 边界

- v5 不把进度、阶段、长期期限放进 Activity schema；这类数据属于 Task。
- v5 不把声明字段翻译成一套 Activity 私有 C# 逻辑。
- Validation Graph 不能写属性、标签、黑板、Task 或 Record。
- Script Graph 不隐藏等待，不在 Activity 结算中递归创建下一层 Activity。
- Activity 的条件失败、未知 Graph、未知参数和非法选项不能静默放过。
- `resolved` 实例不能重新进入活动查询，也不能被第二次结算。
- 设计稿中的 `OfferActivityScope`、投票、`urgency`、命名快照和 Record 节点在代码落地前都是待实现能力。
- 当前代码中 `repeat_policy`、`repeat_cooldown`、`dispatch_policy`、`is_baseline`、`show_condition` 和 `execute_condition` 仍是现行字段；迁移完成前，不能直接删除它们或声称 v5 已生效。

## 6. UAT

```gherkin
Feature: Activity v5 的一次决策
  Activity 只承载一次拍板，结算后不再作为活动返回。

  Scenario: 玩家打开活动并选择可用选项
    Given 作用域实体上挂着一个状态为 "active" 的 Activity 实例
    And 活动定义包含一个通过 Validation Graph 的选项
    When 玩家从拥有权限的 seat 提交该选项
    Then 系统只执行该选项的 Script Graph
    And Activity 实例进入 "resolved"
    And 活动查询不再返回该实例

  Scenario: 条件不满足的选项显示为不可操作
    Given 活动包含一个有 "enable_when" 的选项
    And 该选项的 Validation Graph 返回 false
    When 玩家打开活动面板
    Then 该选项仍显示
    And 该选项不可提交
    And 面板显示由 Validation Graph 返回的阻塞原因

  Scenario: 活动结算把长期后果交给 Task
    Given Activity 的选项结算图声明创建一个 Task
    When 玩家选择该选项
    Then Activity 完成一次结算
    And 一个新的 Task 实例被创建
    And Activity 不会再创建第二层 Activity

Feature: Activity v5 的作用域和投票

  Scenario: 同一活动按玩家作用域分别存在
    Given 活动的作用域类型为 "per_player"
    And 两个玩家各自有一个代表实体
    When TriggerGraph 向两个玩家发射该活动
    Then 每个玩家各得到一个活动实例
    And 两个实例的 ScopeHost 分别指向对应代表实体

  Scenario: 共享活动按投票规则结算
    Given 一个 shared Activity 绑定多个投票者
    And 每个投票者的权重在开票时被冻结
    When 投票达到规则或投票超时
    Then 系统按规则选出一个选项或明确报告未达成
    And 结算只执行一次

  Scenario: 非法配置在加载期失败
    Given Activity 引用了不存在的 Graph 或缺少必需参数
    When 配置包加载
    Then 整个配置包拒绝加载
    And 错误消息包含 Activity id、Graph id 和缺失键
    And 系统不会用默认值静默继续
```
