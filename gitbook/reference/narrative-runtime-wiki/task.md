# Task 任务：挂在目标列表里的持续进度

把某条补给线接通、拿下某座城、招到某位英雄——Task 是跨周期的持续目标：有进度、有完成条件、有失败条件，一直挂在目标列表里直到了结。和 Activity 的分工一句话：**Activity 一次拍板，Task 一段旅程**。一次活动的选项后果如果需要长期追踪，结算效果就用 `task.create` 生成一条 Task 接棒。

合同在 issue #774（已关闭，代码已落地）。本页讲玩家看到什么、作者怎么配。

## 玩家看到什么

目标追踪面板里的一行：任务名、当前要做什么（目标条目）、进度。任务由若干 objective 组成，`completion_rule` 决定全部完成还是任一完成即了结。状态变化（开始/完成/失败）是事实源 `task.state_changed`，供其它系统订阅。

## 作者写法

**声明路径**：mod 的 `config_catalog.json` 加 `Tasks/tasks.json`（ArrayById）。

**写定义**：每条任务必填 `id` / `display_name` / `summary` / `start_policy` / `completion_rule` / `objectives`。目标条目最常用的 `kind: "signal"`——等一个语义信号到了就勾掉：

```json
{
  "id": "showcase.task.hold",
  "display_name": "决议：按兵不动",
  "start_policy": "automatic",
  "completion_rule": "all",
  "objectives": [
    { "id": "hold_logged", "kind": "signal", "title": "等待下一次周期结算复查补给状态。", "signal_key": "showcase.activity.hold_logged" }
  ]
}
```

**谁来创建任务**：活动结算效果 `task.create`（参数 `task_id`），或代码直接 `TaskRuntimeService.OfferOrStart`。

**界面**：PanelKit panelType `objective` + DataPlane `TaskObjectiveWebUiTopicProducer`，模板在 `mods/showcases/panel_kit_task_objective_showcase/`（纯 JSON profile，RTS/4X 语义都在 profile 里）。

## 运行时行为速查

- 实例物化为实体（与 Activity 同款纪律：真相只有实体一处）；
- 运行时快照持久化信号计数与累加器（`TaskRuntimeSnapshot.Signals / Accumulators / NextInstanceId`），存档 domain `task`；
- 桥接安装器注册 fact source `task.state_changed` 与 effect `task.create`——这是当前生产环境仅有的两个跨系统任务键，活动的定义可以放心引用；
- 旧 Core `Gameplay/Quests/`（QuestRuntimeService 等）已退役，不要往那写。

## 入口与验收

| 项 | 值 |
|---|---|
| 总装 showcase | `narrative` / `narrative_frontend`（任务与剧情同台） |
| 面板 showcase | `panel_kit_task_objective_showcase` |
| 集成测试 | `src/Tests/GasTests/Integration/TaskRuntimeTests.cs`、`ActivityTaskPersistenceTests.cs` |
| 面板案例设计 | [面板典型案例 · 任务/活动面板](../../architecture/panel-cases/panel-quests.md) |
