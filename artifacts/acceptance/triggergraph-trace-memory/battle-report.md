# TriggerGraph 调试内存验收记录

## 概述

这次验收使用现有可玩场景“夜袭三波”。玩家照常进入战圈、迎战袭击者；玩法作者可以在同一局里只观察一个 TriggerGraph 入口。关闭调试时，每个挂载点不再预留 2048 条记录区，打开哪个入口才为哪个入口分配，关闭后立即释放并清掉旧记录。

## 结构

- 可玩入口：`night_raid`，加载 `MapTriggerNightRaidMod`、`NightRaidShowcaseMod` 和 `AgentBridgeMod`
- 观察入口：`ludots.graph.debug`
- 本次目标：`Graph.NightRaid.Flow / on_kill_tool`
- 运行后端：`Codegen`
- 画面证据：`night-raid-live.png`
- 请求与结果：`trace.jsonl`
- 操作路径：`path.mmd`

## 详情

- 玩家看到进度面板进入 `STAGE 2`，战圈内生成三名袭击者，游戏主循环持续运行。
- 第一次 `list` 返回 9 个挂载点，全部是 `capacity=2048`、`allocatedCapacity=0`。
- 为 `on_kill_tool` 打开 `NodeAndPins` 后，只有该入口变成 `allocatedCapacity=2048`。
- 触发 `NightRaid.KillTool.Used` 后没有 trigger error，`drain` 读到 18 条记录；节点从 `kt_scope` 运行到 `kt_done`，并带回整数和实体引脚。
- 关闭 trace 后，该入口回到 `allocatedCapacity=0`、`latestSequence=0`、`droppedCount=0`，再次 `drain` 返回空数组。
- `/health` 的 `pumpCount` 从 2084 增长到 15259，说明取证期间游戏主循环一直在运行。
- 自动化回归共 5 项通过，覆盖关闭状态、万人 trace 对象、开关生命周期、环形覆盖和 AgentBridge 的 `list → configure → configure` 合同。

## 场景

```gherkin
Feature: 调试只为正在观察的图入口占用记录内存

  Scenario: 玩家在夜袭中不打开调试
    Given 我进入“夜袭三波”并看到三名袭击者
    When 所有图入口的 trace 都是关闭状态
    Then 战斗继续运行
    And 9 个挂载点的 allocatedCapacity 都是 0

  Scenario: 玩法作者只观察一个入口
    Given 夜袭地图有多个 TriggerGraph 入口
    When 我只给 on_kill_tool 打开 NodeAndPins
    And 我触发一次 NightRaid.KillTool.Used
    Then 我能读到该入口的节点和引脚记录
    And 该入口显示 allocatedCapacity 为 2048

  Scenario: 玩法作者结束观察
    Given on_kill_tool 已经产生 18 条记录
    When 我把该入口的 trace 设为 off
    Then allocatedCapacity 回到 0
    And 旧记录、序号和丢弃数都被清空
```

## 边界

- 本次只关闭 trace 记录区的默认预分配，不宣称 execution slots 已完成 SoA。
- 万人测试构造了 10,000 个关闭状态的 `GraphDebugTrace`，证明记录区总分配容量为 0；它不是 10,000 个完整 TriggerGraph 挂载点的帧时间测试。
- `capacity` 是配置上限，`allocatedCapacity` 是当前实际记录区容量，两者不能混写。
- 现有 trace 仍只有序号和步数，没有时间或帧号；一拍内的节点不能描述成逐步动画。

## UAT

- 结果：通过。
- 真机：`night_raid`，PID 72544，AgentBridge 端口 47931。
- trace 开启：`allocatedCapacity=2048`，18 条记录，`triggerErrors=0`。
- trace 关闭：`allocatedCapacity=0`，`events=[]`。
- 定向测试：5/5 通过。
