# Task 任务：跨周期追踪

状态：当前实现与 v5 目标并列说明。现行代码仍是 Provider 驱动的 Task；v5 设计要求把行为判定移到 Graph。两者没有在本页里偷偷合并。

## 1. 概述

Task 表示一段持续旅程：补给线、城市建设、护送、击杀目标、外交协议。它可以挂在目标列表里跨多个周期追踪，直到完成、失败或放弃。

和 Activity 的分工：

- Activity 是一次拍板，选完就结算。
- Task 是一段旅程，状态和进度会持续存在。
- Dialogue / Sequencer 负责对白和演出。
- Record 负责保存已经发生过的事实。

Activity 结算后如果还要追踪结果，应创建 Task，而不是把 Activity 拉长。

## 2. 结构

### 2.1 玩家看到什么

目标面板显示任务名、摘要、目标行、当前进度和状态。运行时真相在带 `TaskInstanceCm` 的 entity 上，面板只读投影。

当前实现的主要入口：

- [`src/Core/Gameplay/Tasks/TaskDefinitions.cs`](../../../src/Core/Gameplay/Tasks/TaskDefinitions.cs)
- [`src/Core/Gameplay/Tasks/TaskRuntimeService.cs`](../../../src/Core/Gameplay/Tasks/TaskRuntimeService.cs)
- [`src/Tests/GasTests/Integration/TaskRuntimeTests.cs`](../../../src/Tests/GasTests/Integration/TaskRuntimeTests.cs)
- [`src/Tests/GasTests/Integration/ActivityTaskPersistenceTests.cs`](../../../src/Tests/GasTests/Integration/ActivityTaskPersistenceTests.cs)

### 2.2 v5 的责任分层

| 层 | 责任 |
|---|---|
| Task 定义 | 身份、展示元数据、目标行、可选的机械超时 |
| Task 运行时 | entity 状态、终态不可逆、存档、只读面板投影 |
| Graph | 订阅事件、读条件、更新进度、完成/失败/放弃任务 |

Task 定义不应再拥有一套“自己判断什么时候完成”的解释器。需要判断“击杀数是否达到 3”“稳定度是否低于阈值”时，由 Graph 读取属性、事件数据或 Record，再调用任务状态操作。

### 2.3 当前实现和 v5 目标的差异

| 项目 | 当前 `main` | v5 目标 |
|---|---|---|
| 完成规则 | `completion_rule` 由 `TaskRuntimeService` 判断 | 由 Graph 选择何时完成 |
| 信号进度 | `signal_key`、信号计数和累加器由运行时维护 | Graph 读取事件并写入正式的进度数据 |
| 条件 | `condition_key` 进入 Provider 条件 | Validation Graph 只读判断 |
| 跨系统通知 | `task.state_changed` Provider source | 作为可订阅的领域事件接入 Graph |
| 创建任务 | `task.create` Provider effect 或 `OfferOrStart` | Graph 的 `CreateTask` |

当前字段不是 v5 的新合同，但在迁移完成前也不能从代码或已有数据中直接删除。

## 3. 详情

### 3.1 当前配置入口

当前声明路径仍由 mod 的 `config_catalog.json` 指向 Task 配置。现有字段和校验以 [`src/Core/Gameplay/Tasks/TaskDefinitions.cs`](../../../src/Core/Gameplay/Tasks/TaskDefinitions.cs) 为准。

当前运行时仍会读取：

```json
{
  "id": "showcase.task.hold",
  "display_name": "决议：按兵不动",
  "start_policy": "automatic",
  "completion_rule": "all",
  "objectives": [
    {
      "id": "hold_logged",
      "kind": "signal",
      "title": "等待下一次周期结算复查补给状态。",
      "signal_key": "showcase.activity.hold_logged"
    }
  ]
}
```

这段配置只能说明当前实现，不是 v5 推荐写法。当前运行时的状态变化会通过 `TaskStateChanged` 和 `task.state_changed` Provider source 发出，相关代码在 [`src/Core/Gameplay/Tasks/TaskRuntimeService.cs`](../../../src/Core/Gameplay/Tasks/TaskRuntimeService.cs) 与 [`src/Core/Gameplay/Tasks/TaskBridgeProviders.cs`](../../../src/Core/Gameplay/Tasks/TaskBridgeProviders.cs)。

### 3.2 v5 目标写法

v5 的 Task 定义只保留展示和机械状态：

```json
{
  "id": "quest.kill_murlocs",
  "display_name": "清剿鱼人",
  "summary": "击杀 3 只鱼人。",
  "objectives": [
    { "id": "kill", "title": "击杀鱼人 x3" }
  ]
}
```

完成行为由 TriggerGraph 表达：

```jsonc
{
  "id": "Graph.Quest.MurlocWatch",
  "kind": "TriggerGraph",
  "entries": [
    { "label": "on_kill", "event": "Unit.Died", "start": "victim", "refire": "restart" }
  ],
  "nodes": [
    { "id": "victim", "op": "LoadEntryPayloadEntity", "payloadKey": "victim" },
    { "id": "is_murloc", "op": "HasTag", "tag": "Creature.Murloc" },
    { "id": "complete", "op": "CompleteTask", "taskId": "quest.kill_murlocs" }
  ]
}
```

上面的 `CompleteTask` 是 v5 目标节点，当前 Graph VM 尚未提供 Task 状态操作。

### 3.3 Activity 与 Task 串联

```text
事件 -> TriggerGraph 读取条件 -> 更新 Task -> CompleteTask -> OfferActivity
```

最后一步是可选的领取、确认或下一次决策。Activity 负责拍板，Task 负责持续记录。

## 4. 场景

### 4.1 进度型任务

击杀事件每发生一次，Graph 更新任务进度。达到目标后，Graph 设置目标行完成并完成 Task。

### 4.2 协议型任务

停战条约的有效期和条款属于 Task。签约和撕约属于 Activity；Activity 的结算图修改协议状态，并可写 Record。

### 4.3 Activity 结算接棒

玩家在 Activity 中选择“开始工业建设”。结算图创建 `focus.industry_1` Task。70 天后由时间事件触发 Graph 完成 Task，再解锁下一项。

## 5. 边界

- Task 不变成 Activity；它不要求玩家在每次进度变化时重新拍板。
- Task 不在定义对象里隐式执行条件求值。
- `completion_rule`、`signal_key`、`condition_key` 是当前代码仍支持的旧字段，不是 v5 新增的推荐合同。
- v5 迁移完成前，不能删除当前字段、Provider source、Provider effect 或已有存档格式。
- 完成、失败和放弃必须是显式状态操作；未知 Task id、目标 id、Graph id 或参数必须失败关闭。
- 终态不可逆，已完成任务不能再次完成，已失败任务不能静默恢复成进行中。
- 任务进度不能依赖面板缓存；面板只读 entity 和已登记的数据源。

## 6. UAT

```gherkin
Feature: 玩家追踪一段持续任务

  Scenario: 玩家在目标面板看到任务进度
    Given 一个 Task 实例已经挂在作用域实体上
    When 玩家打开目标面板
    Then 玩家看到任务标题和目标行
    And 面板显示当前状态
    And 面板没有自行计算另一份任务状态

  Scenario: 领域事件让 Graph 完成任务
    Given 一个 Task 正在追踪击杀目标
    And TriggerGraph 订阅击杀事件
    When 玩家完成最后一个目标
    Then Graph 更新目标行
    And Graph 显式完成 Task
    And Task 只发出一次完成状态变化

  Scenario: Activity 结算创建 Task
    Given 玩家在 Activity 中选择开始长期建设
    When 选项结算 Graph 执行
    Then Activity 完成一次结算
    And 一个建设 Task 被创建
    And 玩家可以在目标面板继续追踪建设 Task

  Scenario: 终态不能被悄悄改回
    Given 一个 Task 已经进入 "completed" 或 "failed"
    When 另一个 Graph 再次提交相反的状态
    Then 状态操作被拒绝
    And 面板保留原终态

  Scenario: 旧字段在迁移完成前仍按现状处理
    Given 当前配置仍包含 "completion_rule" 或 "signal_key"
    When 当前 Task loader 读取配置
    Then 系统按当前代码合同处理这些字段
    And 文档把它们标成迁移中的旧字段
```
