# 路线到 MassNavigation 执行

> [功能目录](README.md) · [体系总合同](../navmesh-ssot.md)

## 1. 概述

路由层决定 waypoint；MassNavigationFlow 决定单位怎样执行、避让和到达。精确路线的小队与 flow-only 大军可以共存，但二者不能各自维护一套移动执行组件。

## 2. 结构

```text
Order -> PathServiceRouter/AutoPathService -> waypoint cursor
      -> MassNavigationMovePlanExecutionSink -> MassNavigationFlow target
```

## 3. 详情

### 3.1 编辑器功能

- AgentType 面板显示是否进入 route sink、当前 profile 和 route domain。
- 路径预览标出 waypoint、当前 cursor、执行目标和到达容差。
- 未声明 route profile 的 Agent 明确显示 flow-only，而不是显示假路线。

### 3.2 工具功能

- route 只产出路径/waypoint；执行 sink 只接收正式 MovePlan。
- `MassNavigationMovePlanExecutionSink` 使用已有 per-agent target API；缺少 `MassNavigationAgentIndex` 时返回 typed failure。
- road/waterway 只保留各自 route policy，不能在 Mod 内复制移动 runtime。

### 3.3 Runtime 功能

- waypoint 推进、settle、arrival 和临时 avoidance 归 MassNavigationFlow。
- 路径过期时执行层暂停或请求重算，不能继续穿过已关闭的结构。
- route result 不直接写 `WorldPositionCm`，也不直接移动实体。

## 4. 场景与 Showcase 设计

### 一句话与目标用户

小队走精确路线，大军继续用流场，两个系统在一张地图上各自发挥作用。

### 主循环

玩家同时选中道路小队和开放地形大军，发出同一目标命令；HUD 显示小队 waypoint 和大军 FlowWindow。切换小队 profile 后路线域变化，关闭道路后小队重新规划而大军仍按 flow 执行。

### 消融对照

同一地图中分别移除 route sink 和移除 MassNavigationFlow 执行；前者只影响已声明精确路线的单位，后者应让 flow-only 单位明确失败。不能把测试回调当成执行。

### 解释层

显示 route domain、waypoint cursor、per-agent target、FlowWindow、到达人数和失败原因。

### 旋钮清单

| 旋钮 | 范围 | 演示什么 |
|---|---|---|
| Agent profile | 已声明 profile | 谁进入精确路线 |
| route mode | Graph / Mesh / Auto | 路线来源 |
| 小队规模 | 场景合法数量 | 精确路线与群体执行边界 |
| 目标点 | 道路 / 开放地形 | 两种执行结果 |

### 场景结构

主演示道路小队和开放地形大军；子场景是 route sink 缺绑定、路径刷新和 settle 后继续推进。首屏：“框选两队，右键同一目标，看它们走不同执行路径。”

### 门户资产

截图保留两队的 route/flow 标记和实际到达状态；预览读取 pathing、MassNavigationConfig 和场景绑定。

### 反向 API 审计

需要跨 route 与 execution 的统一 explanation、到达计数和过期路径事件。现有 MovePlanning、MassNavigation sink、PathStore 和 MassNavigationFlow 是复用入口。

## 5. 边界

本页不定义 route 算法或 NavMesh 几何；也不把 MassNavigation 扩展成第二个 NavMesh 查询器。临时避障不触发 NavTile 重烤。

## 6. UAT

```gherkin
Feature: 路线结果进入统一移动执行

  Scenario: 声明了精确路线的单位执行 waypoint
    Given 小队 profile 已在 pathing.json 声明
    When 玩家命令小队前往目标
    Then 小队收到 route waypoint
    And MassNavigationFlow per-agent target 随 waypoint 推进
    And 小队到达目标

  Scenario: 缺少执行绑定时失败
    Given 单位没有 MassNavigationAgentIndex
    When 路线尝试进入执行层
    Then 系统返回 typed failure
    And 不创建临时执行组件
```

## 7. 现状与 TODO

主线已有 route-to-MassNavigationFlow sink、waypoint cursor 和 road execution 边界；profile 双路真实 showcase、全队到达和路径过期时的执行行为仍需验收。原始材料见 `reference/routing-to-mass-execution.md` 和 `reference/move-planning-mass-navigation-flow-road-execution.md`。

| 责任层 | TODO | 完成判据 |
|---|---|---|
| 编辑器 | route/flow 执行解释面板 | 玩家能区分两种执行 |
| 工具 | route result 到 MovePlan 的诊断 | 缺绑定不静默跳过 |
| Runtime | 过期路径、settle、全队到达回归 | 不穿障碍且正确完成 |
| Showcase | 双 profile 同图真实移动 | Agent Bridge 取证通过 |
