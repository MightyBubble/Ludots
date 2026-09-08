# 拓扑与逻辑地形

> [功能目录](README.md) · [体系总合同](../navmesh-ssot.md)

## 1. 概述

Grid、HexGrid 和 NodeGraph 可以并存，但不是同一条烘焙路径。Grid/Hex 通过 `LogicTerrainField` 进入 NavMesh；NodeGraph 负责图路由，不占用 NavMesh 地形输入。视觉高度只有在显式 projection 时才改变逻辑走性。

## 2. 结构

```text
Grid/Hex source -> LogicTerrainField -> walk mask -> NavTile
NodeGraph data ---------------------> graph route
visual height --explicit projection-> LogicTerrainField
```

## 3. 详情

### 3.1 编辑器功能

- Board 面板明确显示 `Grid`、`HexGrid` 或 `NodeGraph`，并切换对应的数据入口。
- Grid/Hex 视图分别编辑高度、blocked、water、ramp 和 `areaId`；视觉高度与逻辑高度分开显示。
- NodeGraph 编辑器只编辑节点、边和容量，不伪装成 NavMesh 面板。

### 3.2 工具功能

- Grid 使用 `MutableGridLogicTerrainField`，Hex 使用 `VertexMapLogicTerrainField`；NodeGraph 直接走图服务。
- projection 必须是显式命令，输入 revision 和投影参数进入 build hash。
- 同一逻辑输入的 Grid/Hex 结果可做拓扑对照；不支持的组合直接失败。

### 3.3 Runtime 功能

- NavBakeContext 只消费逻辑地形；presentation 使用连续视觉高度，但不会自动改写走性。
- NodeGraph 路由使用 `GraphEdgeProjectionQuery`、goal snap 和图边约束，不创建假 NavTile。

## 4. 场景与 Showcase 设计

### 一句话与目标用户

换地形表现不会偷偷改变“能不能走”。

### 主循环

作者在 Grid 和 Hex 中查看同一逻辑障碍，再单独抬高视觉地面。只有执行显式投影并重新烘焙后，走性才变化。NodeGraph 子场景展示长距离道路走图、局部区域走 NavMesh。

### 消融对照

只改视觉高度时路径不变；执行 projection 后路径和 NavTile revision 才变化。NodeGraph 对照中关闭图数据会明确失败，不退回直线。

### 解释层

显示拓扑、逻辑高度、视觉高度、projection revision、walk mask 和 route domain。

### 旋钮清单

| 旋钮 | 范围 | 演示什么 |
|---|---|---|
| 拓扑 | Grid / Hex / NodeGraph | 数据入口如何改变 |
| 视觉高度 | 场景高度预设 | 只改变表现的情况 |
| projection | 关闭 / 执行 | 何时走性真正变化 |
| 路由域 | Graph / Mesh | 长程与局部路线分工 |

### 场景结构

主演示 Grid 的视觉/逻辑分离；子场景 Hex parity、NodeGraph road 和显式 projection。首屏：“先抬高地面，再点 projection，看哪一步真的改变路径。”

### 门户资产

截图同时保留视觉高度和 walk mask；预览读取 board/source 配置。

### 反向 API 审计

需要 topology-aware source adapter、显式 projection 记录和统一 Graph/Mesh route result。现有 `LogicTerrainField`、GraphQuery services 和 NavBakeService 是复用入口。

## 5. 边界

本页不定义区域代价、水深或障碍实体。`areaId` 交给区域代价页；水语义交给水面页；NodeGraph 的动态边权交给路由域页。

## 6. UAT

```gherkin
Feature: 拓扑和视觉地形不会互相偷改

  Scenario: 视觉高度独立于走性
    Given 作者已烘焙一张 Grid 地图
    When 作者只修改视觉高度且不执行 projection
    Then NavTile revision 不变
    And 原路径仍可查询

  Scenario: NodeGraph 缺数据时失败
    Given 作者选择 NodeGraph board 但没有图数据
    When 作者请求路径
    Then 页面显示图数据缺失
    And 系统不生成直线替代路径
```

## 7. 现状与 TODO

Grid/Hex `LogicTerrainField`、显式视觉投影和 NodeGraph 不烘焙规则已有主线证据；正式 source 单一事实源、projection 的编辑器入口和混合路由验收仍需补齐。原始证据见 `reference/logic-terrain-and-topology.md`。

| 责任层 | TODO | 完成判据 |
|---|---|---|
| 编辑器 | 拓扑与视觉/逻辑双层面板 | 作者不会误把表现高度当走性 |
| 工具 | 统一 source snapshot 与 projection hash | CLI/Bridge 结果一致 |
| Runtime | Grid/Hex/Graph 路由边界回归 | 不支持组合 fail-fast |
| Showcase | 三种拓扑可操作对照 | 玩家能解释各自用途 |
