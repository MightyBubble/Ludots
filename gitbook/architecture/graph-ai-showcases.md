# Graph AI Showcase Responsibilities

## 1 概述

Graph AI showcase 分成两类入口，不能再混着讲。

Capability showcase 给新玩家看“这个能力到底怎么用”。它只回答三件事：玩家看到什么、场景自己发生什么、反馈是否足够清楚。

Benchmark showcase 给验收人看“这个能力能不能扛规模”。它只回答规模多少、耗时多少、有没有分配、有没有丢实例。

## 2 结构

| Showcase | 类型 | 入口 | 主责 | 不负责 |
|---|---|---|---|---|
| Graph Level Blueprint Capability | Capability | `graph_level_blueprint_raylib` | 展示玩家把 token 推进触发区后，关卡图按顺序推动门、巡逻点、信标、出口 | 不证明 FSM/BT 语义，不作为性能结论 |
| Graph RTS Stance FSM Capability | Capability | `graph_stance_fsm_raylib` | 展示小队根据战场情况切换站姿并移动到对应目标 | 不证明行为树任务流，不作为性能结论 |
| Graph Behavior Tree Capability | Capability | `graph_complex_bt_raylib` | 展示小队选择任务、执行倒计时、完成后重新进树 | 不证明关卡蓝图，不作为性能结论 |
| Graph AI 50k Benchmark Field | Benchmark | `graph_stress_field_raylib` | 展示 50,000 个可见实体同时跑 FSM+BT，并给出耗时、0 分配、无丢点证据 | 不承担教学场景职责 |

## 3 详情

Capability showcase 的画面必须小、清楚、单一职责。玩家不需要读技术报告，只要能看见场景在动，并且能把画面反馈和能力对应起来。

Benchmark showcase 可以保留数字面板，但画面仍要可检查：每个点都代表一个真实 ECS graph brain，点必须可见、会动、不能被渲染缓冲静默丢掉。

前三个 capability showcase 不声明 benchmark hot path，也不在画面里展示性能数字。性能证明只从 Graph AI 50k Benchmark Field 和对应 benchmark test 读取。

## 4 场景

Level Blueprint 的最小场景是“玩家触发关卡事件，世界立刻反馈”：玩家把黄色 token 推进当前触发区，graph 判断条件成立后，门打开，巡逻点响应，目标信标变化，出口解锁。它不是播片，必须让玩家看见“我触发了什么，世界发生了什么”。

RTS Stance FSM 的最小场景是“小队站姿决定行为”：近敌攻击、受伤回撤、士气高防守、无威胁观察。玩家应该看到不同小队走向不同目标。

Complex BT 的最小场景是“任务选择和任务生命周期”：小队选择掩体、压制目标、呼叫支援、侦察路线；任务有持续时间，完成后重新选择。

50k Benchmark Field 的最小场景是“规模验收”：画面上必须有 50,000 个可见点，点在移动，FSM/BT 分支都有命中，热路径 0 分配、Gen0 不增长、没有 primitive drop。

## 5 边界

Capability showcase 失败标准：玩家看不出实体为什么动、状态反馈和场景动作对不上、一个入口里塞了多个无关能力。

Benchmark showcase 失败标准：实体数不足、点不可见、点不动、渲染实例丢失、热路径有分配、Gen0 增长、耗时数据缺失。

当前拆分不新增 graph 语义，也不改变 ECS benchmark 热路径；只统一入口分类、展示文案和验收叙述。

## 6 UAT

```gherkin
Feature: Graph AI capability showcases

  Scenario: 新玩家打开关卡蓝图能力展示
    Given 玩家启动 Graph Level Blueprint Capability
    When 玩家把黄色 token 依次推入四个高亮触发区
    Then 玩家能看到门、巡逻点、信标、出口只在对应触发后变化
    And 玩家能把这些变化理解为同一个关卡蓝图在响应玩家触发

  Scenario: 新玩家打开 RTS 站姿 FSM 能力展示
    Given 玩家启动 Graph RTS Stance FSM Capability
    When 四个小队读入各自的距离、生命和士气
    Then 玩家能看到小队分别执行观察、防守、攻击或回撤
    And 每个小队的移动目标和站姿反馈一致

  Scenario: 新玩家打开行为树能力展示
    Given 玩家启动 Graph Behavior Tree Capability
    When 小队完成当前任务倒计时
    Then 玩家能看到任务完成数量增加
    And 小队重新选择下一个可见任务目标
```

```gherkin
Feature: Graph AI benchmark showcase

  Scenario: 验收人打开 50k 基准展示
    Given 验收人启动 Graph AI 50k Benchmark Field
    When 场景稳定运行多个节拍
    Then 画面中有 50,000 个可见且移动的实体点
    And FSM 和 BT 决策数持续增长
    And primitive drop 为 0
    And 热路径分配为 0
    And Gen0 回收增长为 0
```
