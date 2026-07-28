# Graph Workbench Showcase

## 概述

Graph Workbench 是通用 Graph 编辑与运行调试 showcase，不属于 AI 子系统。它展示同一套底层 Graph 基建怎样被关卡蓝图、GAS 技能蓝图、FSM、行为树共同使用。

玩家打开它后，看到的是一个能编辑、能编译、能观察运行中实体当前节点的工作台，而不是性能数字报告。

## 结构

- `Graph`：底层执行图，负责节点、边、校验、编译、运行结果。
- `Level Blueprint`：关卡事件图，用 Graph 描述触发器、机关、门、巡逻和出口。
- `GAS Skill Blueprint`：技能图，用 Graph 描述资源校验、目标选择、效果应用。
- `FSM`：状态图，状态和迁移可以绑定底层 Graph 实现。
- `Behavior Tree`：任务树，条件和任务可以绑定底层 Graph 实现。
- `Graph Workbench`：CEF 内嵌 ReactFlow 编辑器，负责编辑文档、发送编译命令、显示运行调试状态。

## 详情

Workbench 复用现有 CEF Browser Runtime、WebUI DataPlane、ReactFlow、GASGraph `GraphCompiler` 和 `GraphValidator`。浏览器端只保存草稿和用户操作，运行真相来自 C# DataPlane topic。

FSM 和行为树节点双击时，只能进入已经绑定的实现 Graph。没有绑定时必须显示明确提示，不能静默忽略。

运行调试只发布选中实体当前节点、少量实体列表和聚合信息。5 万实体类场景不得把全量逐节点轨迹推到浏览器。

## 场景

关卡蓝图场景：玩家查看开门流程，看到触发器、开门、巡逻、信标按 Graph 顺序连接。

技能 GAS 场景：玩家查看火球技能消耗，看到蓝耗和冷却由同一套 Graph 节点计算。

FSM 场景：玩家查看 RTS 姿态，双击 Return Fire 状态进入它的实现 Graph。

行为树场景：玩家查看压制推进任务树，双击任务节点进入底层 Graph 实现。

## 边界

Graph Workbench 不新增 Graph registry，不新增配置加载管线，不新增浏览器私有事件系统，不把 GraphAiShowcaseCommon 提升成正式基建。

第一版不做多人协同、不做完整磁盘保存、不做全量 GAS op 面板、不做热路径断点单步。编译失败必须留在草稿态，运行中版本继续使用上一次成功编译结果。

## UAT

```gherkin
Feature: CEF 内嵌 ReactFlow 通用 Graph 工作台

  Scenario: 玩家打开工作台首屏
    Given 玩家启动 Graph Workbench showcase
    When 首屏加载完成
    Then 玩家看到 Graph、FSM、BT 三种编辑模式
    And 玩家看到关卡蓝图和 GAS 技能蓝图作为 Graph 文档
    And 首屏不以性能数字作为主要内容

  Scenario: 玩家从 FSM 节点进入实现 Graph
    Given 玩家打开 RTS 姿态 FSM
    When 玩家双击带实现图绑定的 Return Fire 状态
    Then 画布切换到 Return Fire 的实现 Graph
    And 面包屑可以返回 RTS 姿态 FSM

  Scenario: 玩家从行为树任务进入实现 Graph
    Given 玩家打开复杂行为树
    When 玩家双击带实现图绑定的任务节点
    Then 画布切换到该任务的底层 Graph
    And 运行反馈仍显示选中实体当前节点

  Scenario: 编译失败不污染运行版本
    Given 玩家把 Graph 入口改成不存在的节点
    When 玩家点击 Compile
    Then 编译面板显示错误
    And 运行中版本仍停留在上一次成功编译的版本

  Scenario: 没有实现图绑定时不静默失败
    Given 玩家选中没有实现图绑定的节点
    When 玩家双击该节点
    Then 画布仍停留在当前图
    And 检查器显示该节点没有实现图绑定
```