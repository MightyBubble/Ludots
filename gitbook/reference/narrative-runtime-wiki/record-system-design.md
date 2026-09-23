# Record 系统设计：通用事件日志

状态：设计稿。Record 是通用审计轨迹，负责保存“曾经发生过什么”。当前仓库还没有这套运行时，本文是目标合同，不是现成实现。

## 1. 概述

Record 用来保存跨系统的历史事实：战报、对话记录、编年史、交易流水、活动结算结果。它和 Presentation cue、Activity、Task 的区别很简单：

- Presentation cue 只管“现在显示什么”。
- Activity / Task 只管“现在在做什么”。
- Record 只管“曾经发生过什么”。

Record 的核心要求不是展示形式，而是审计属性：追加写入、不可改写、按类别查询、可供面板订阅。

当前代码里还没有 `RecordLog`、`WriteRecord`、`QueryRecordCount` 或 `QueryRecordExists`，所以本文属于新增能力设计。复用入口仍然是 Graph VM、现有存档域和面板投递链路，不能自己再造一套历史系统。

## 2. 结构

### 2.1 数据模型

挂在 world 或 session entity 上的组件：

```text
RecordLog（组件）
  capacity: int
  entries: 环形缓冲

RecordEntry（结构体）
  tick        -> 发生时刻
  category    -> 类别 id
  source      -> 写入者
  text_key    -> 本地化键
  text_args   -> 文本参数
  entity_refs -> 相关实体
  data        -> 结构化载荷
```

### 2.2 Graph 节点

#### WriteRecord

```json
{
  "op": "WriteRecord",
  "category": "combat",
  "textKey": "record.battle_won",
  "args": { "enemy_count": 3000, "location": "北境平原" }
}
```

职责：

- 把一条记录追加进 `RecordLog`。
- 类别和 text key 走编译期符号或加载期校验。
- 实体引用和参数继续走现有 Graph 参数通道。

#### QueryRecordCount

```json
{ "op": "QueryRecordCount", "category": "combat" }
```

职责：

- 返回某类记录的数量。
- 支持按 source、tick 范围等条件过滤。

#### QueryRecordExists

```json
{ "op": "QueryRecordExists", "category": "diplomacy", "textKey": "record.treaty_broken" }
```

职责：

- 返回某条记录是否存在。
- 适合做“曾经发生过某件事才放行”的条件门。

### 2.3 面板

Record 的面板只消费 topic，不自己维护历史源。

```json
{
  "panelId": "hud.chat_log",
  "panelType": "record",
  "topic": "records.chat",
  "profile": { "category": "chat", "max_entries": 100, "order": "desc" }
}
```

同一套面板可以订不同类别：

- `chat`
- `combat`
- `chronicle`
- `activity`

## 3. 详情

### 3.1 自动写入点

| 事件 | category | 示例键 |
|---|---|---|
| Activity resolve | `activity` | `record.activity_resolved` |
| Activity timeout | `activity` | `record.activity_timeout` |
| Task complete | `task` | `record.task_completed` |
| Task fail | `task` | `record.task_failed` |
| Dialogue line | `chat` | `record.dialogue_line` |
| Dialogue ended | `chat` | `record.dialogue_ended` |
| Sequencer completed | `chronicle` | `record.cutscene_played` |
| Treaty signed | `diplomacy` | `record.treaty_signed` |
| Treaty broken | `diplomacy` | `record.treaty_broken` |
| Battle result | `combat` | `record.battle_result` |

### 3.2 典型写法

战斗后写战报：

```jsonc
{
  "id": "Graph.Battle.AfterCombat",
  "kind": "TriggerGraph",
  "entries": [{ "label": "on_battle_end", "event": "Battle.Ended", "start": "write", "refire": "restart" }],
  "nodes": [
    { "id": "winner", "op": "LoadEntryPayloadEntity", "payloadKey": "winner" },
    { "id": "store_count", "op": "StoreArgInt", "argKey": "enemy_losses" },
    {
      "id": "write",
      "op": "WriteRecord",
      "category": "combat",
      "textKey": "record.battle_result",
      "args": { "enemy_losses": 3000, "location": "北境平原" }
    },
    { "id": "done", "op": "HaltReturnInt" }
  ]
}
```

任务完成后写编年史：

```json
{
  "id": "court.king_death",
  "after": { "script": "Graph.Court.RecordDeath" },
  "options": [
    { "id": "eldest_son", "title": "长子继位", "settle": { "script": "Graph.Court.EldestSon" } },
    { "id": "civil_war", "title": "诸侯争霸", "settle": { "script": "Graph.Court.CivilWar" } }
  ]
}
```

条件门读 Record：

```jsonc
{
  "id": "Graph.Gate.IsVeteran",
  "kind": "Validation",
  "nodes": [
    { "id": "count", "op": "QueryRecordCount", "category": "combat" },
    { "id": "thresh", "op": "ConstInt", "intValue": 3 },
    { "id": "cmp", "op": "CompareGtInt" },
    { "id": "done", "op": "HaltReturnInt" }
  ]
}
```

### 3.3 复用链路

Record 只复用现成基础设施：

- Graph VM 负责调用 Graph 节点。
- 存档系统负责持久化 `records` domain。
- 面板系统负责订阅 topic 并显示。
- Activity / Task / Dialogue / Sequencer 只通过 Graph 写入，不各自长一套历史系统。

当前仓库可作为接线参考的代码：

- [`src/Core/NodeLibraries/GASGraph/GasGraphOpHandlerTable.cs`](../../../src/Core/NodeLibraries/GASGraph/GasGraphOpHandlerTable.cs)
- [`src/Core/NodeLibraries/GASGraph/IGraphRuntimeApi.cs`](../../../src/Core/NodeLibraries/GASGraph/IGraphRuntimeApi.cs)
- [`src/Core/Persistence/CoreSaveParticipants.cs`](../../../src/Core/Persistence/CoreSaveParticipants.cs)
- [`src/Core/Gameplay/Sequencer/SequencerRuntime.cs`](../../../src/Core/Gameplay/Sequencer/SequencerRuntime.cs)

## 4. 场景

- 战斗结束后补战报。
- 协议签订或撕毁后留档。
- 对话逐句入库，给聊天面板和回放用。
- 老兵活动按战斗史解锁。
- 任务完成后给编年史写一条可检索历史。

## 5. 边界

- Record 不是任务进度，不承载完成态。
- Record 不是对话树，不负责现时演出。
- Record 不是 Activity 面板缓存，不能拿来替代当前活动实例。
- 写入必须失败关闭：缺少类别、缺少 text key 或目标组件时，不能默默吞掉。
- 追加写入和容量淘汰必须在同一次组件操作里完成，不能拆成两步。
- 当前仓库里没有这套能力，所以这份文档只能作为设计合同和接线依据。

## 6. UAT

```gherkin
Feature: Record 追加和查询

  Scenario: 战斗结束写入一条记录
    Given 世界上挂着一个 RecordLog 组件
    And Graph 提供 WriteRecord 节点
    When 战斗结算 Graph 调用 WriteRecord
    Then RecordLog 追加一条 combat 记录
    And 该记录保留 text_key、text_args 和 entity_refs

  Scenario: 老兵门读取记录数量
    Given RecordLog 中已经有三条 combat 记录
    When Validation Graph 调用 QueryRecordCount
    Then Graph 得到的数量为 3
    And 活动可以据此决定是否放行

  Scenario: 撕约后查询存在性
    Given RecordLog 中存在 diplomacy 类别的撕约记录
    When Validation Graph 调用 QueryRecordExists
    Then Graph 返回 true
    And 该结果可以驱动后续活动显示

  Scenario: 面板按类别订阅
    Given 面板 profile 的 category 为 chat
    And Record topic producer 已注册
    When 系统写入一条 chat 记录
    Then 面板收到这条记录
    And 面板不会收到 combat 类别记录

  Scenario: 记录满了先挤最老的
    Given RecordLog 已经达到 capacity
    When 再写入一条新记录
    Then 最老的一条记录被移出
    And 新记录成功写入
```
