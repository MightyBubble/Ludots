# 路由域选择与动态边权

> [功能目录](README.md) · [体系总合同](../navmesh-ssot.md)

## 1. 概述

`PathingConfig` 按 Agent 选择 Graph、Mesh 或 Auto。Graph 适合道路、航线和长距离连接，Mesh 适合连续地形，Auto 根据正式代价比较两者。动态边权是图路由输入，缺少声明的 overlay 必须报错，不能静默降级。

## 2. 结构

```text
AgentType + pathing policy -> PathServiceRouter / AutoPathService
Graph route: projection + tag cost + GraphEdgeCostOverlay
Mesh route: NavQueryService + area cost
                         -> route result
```

## 3. 详情

### 3.1 编辑器功能

- Agent 行显示 `AutoCheapest`、`PreferGraph`、`PreferMesh` 和 graph/mesh 权重。
- NodeGraph 边可显示投影点、容量、静态 tag cost 和动态 overlay 结果。
- Auto 预览列出候选路线、总 cost、预计长度和选择原因。

### 3.2 工具功能

- `GraphEdgeProjectionQuery`、`PolylineGoalSnapQuery` 和 `GraphHybridRouteBuilder` 作为共享服务使用。
- `nodeGraph.useDynamicOverlay=true` 时必须注册 `GraphEdgeCostOverlay`；缺失即 solve error。
- 静态规则先算，动态项按既有公式 `staticCost * (1 + costMul) + costAdd` 合并；不把 cost 倒写进地图 area。

### 3.3 Runtime 功能

- Path result 带 route domain、候选 cost、overlay revision 和 pathing policy version。
- Graph 启发式不满足 admissible 条件时，诊断标出可能非最优；失败不返回直线。
- Mesh 几何缓存与 Graph edge cost overlay 分开失效，动态边权变化不重烘焙 NavTile。

## 4. 场景与 Showcase 设计

### 一句话与目标用户

同一条命令，单位会按自己的任务选择道路或地形。

### 主循环

小队先按 PreferGraph 走道路，大军按 flow 执行；切到 PreferMesh 后小队改走地形。再关闭动态桥梁 overlay，Auto 选择发生变化或明确失败。惊喜时刻是道路被动态加价后，系统绕到连续地形。

### 消融对照

只切换 route policy，NavTile 不变；只改变 overlay，Graph 路线变化，Mesh 几何不变。缺 overlay 的对照必须显示错误，而不是退回静态边权。

### 解释层

显示候选路线、domain、静态 cost、overlay 修正、最终 cost、projection 点和失败原因。

### 旋钮清单

| 旋钮 | 范围 | 演示什么 |
|---|---|---|
| route mode | Auto / Graph / Mesh | 路由域选择 |
| graph bias | 合法策略档位 | Auto 取舍 |
| overlay cost | 场景动态值 | 道路变化如何改路 |
| 目标点 | 道路端点 / 地形区域 | projection 与 nearest 结果 |

### 场景结构

主演示道路与地形分流；子场景是 Auto 选择、边权变化、无 overlay 失败和 nearest-goal snap。首屏：“先让小队走路，再给道路加价，看它是否改变选择。”

### 门户资产

截图保留候选路线和 cost 分解；预览从 `pathing.json`、图资产和 overlay 配置读取。

### 反向 API 审计

需要统一 route explanation、overlay revision 和候选路线只读接口。现有 `PathServiceRouter`、`AutoPathService`、GraphQuery services 和 PathStore 是正式入口。

## 5. 边界

本页不负责 Agent 几何 profile、area 分类或 MassNavigation 执行。GraphEdgeCostOverlay 只影响图边策略，不替代结构障碍和 NavMesh dirty rebuild。

## 6. UAT

```gherkin
Feature: 路由策略按 Agent 和动态边权选择

  Scenario: PreferGraph 使用道路
    Given AgentType 的 route mode 是 PreferGraph
    When 玩家命令小队前往道路终点
    Then 路径结果标记 Graph
    And 结果包含图边投影信息

  Scenario: 动态 overlay 缺失时失败关闭
    Given NodeGraph 声明必须使用动态 overlay
    And 当前没有注册 overlay
    When 玩家请求路径
    Then 查询显示 overlay 缺失
    And 系统不使用静态边权偷偷继续
```

## 7. 现状与 TODO

主线已有 `PathServiceRouter`、`AutoPathService`、Graph projection/snap 和 `AutoCheapest`/`PreferGraph`/`PreferMesh`；动态 overlay 的正式注册、作者解释面和全链 UAT 仍未收口。原始材料见 `reference/routing-to-mass-execution.md`、`reference/graph-query-services.md` 和 `reference/transport-network-asset.md`。

| 责任层 | TODO | 完成判据 |
|---|---|---|
| 编辑器 | 候选路线与 cost 分解面板 | 修改策略后原因可读 |
| 工具 | overlay revision 和严格校验 | 缺 overlay 明确失败 |
| Runtime | dynamic overlay 消费与缓存隔离 | 不重烘焙几何即可换边权 |
| Showcase | Graph/Mesh/Auto 动态对照 | 实际路线和 HUD 同步改变 |
