# 空间尺度与分辨率

> [功能目录](README.md) · [体系总合同](../navmesh-ssot.md)

## 1. 概述

这页定义世界范围、板内坐标、地形块、NavTile、流场窗口和避障网格各自代表什么。`CellCm`、`TerrainChunk`、`NavTile`、`FlowCell` 不是同一个尺度；混用会让地图尺寸、烘焙预算和运行时性能一起失真。

## 2. 结构

```text
WorldExtent -> Board topology/cell -> TerrainChunk/NavTile
            -> MassNavigation FlowWindow/FlowCell -> avoidance hash
```

世界范围由 `Boards[].WidthInMacroTiles`、`HeightInMacroTiles` 和 `GridCellSizeCm` 决定；`ChunkSizeCells` 只服务空间分区；导航 tile footprint 由 terrain/navmesh owner 决定；MassNavigation 的执行窗口和流场分辨率由它自己的配置决定。

## 3. 详情

### 3.1 编辑器功能

- New Board 以目标米数和拓扑为输入，派生 MacroTile、cell、TerrainChunk 和 NavTile 数量。
- 尺度面板同时显示 `CellCm`、世界范围、TerrainChunk、NavTile、FlowWindow 和 FlowCell，标出每个值的 owner。
- 任何不能整除的 FlowWindow、FlowCell 或 hash cell 组合在保存前报错。

### 3.2 工具功能

- estimate 使用同一套尺度派生公式：`worldCm = macroTiles × 256 × cellCm`。
- 工具禁止把 `PartitionChunk` 当 NavTile，也禁止把 `FlowCell` 当 board cell 的别名。
- 所有尺度进入 estimate 输入摘要；缺字段、错误单位和非法倍数 fail-fast。

### 3.3 Runtime 功能

- Board registry、terrain、NavTileStore 和 MassNavigationFlow 使用同一 board 身份，但各自消费自己的尺度。
- 修改 board cell、tile footprint 或世界原点会使相关产物过期；修改 FlowCell 只改变执行层网格，不重烘焙几何。

## 4. 场景与 Showcase 设计

### 一句话与目标用户

地图做大以后，作者仍能看懂每个“格子”到底负责什么。

### 主循环

作者先输入目标地图宽度，再调地形 cell 和 FlowWindow；界面实时显示世界范围、NavTile 数量和局部执行网格。惊喜时刻是把地图扩大后，世界范围增加，但局部 FlowWindow 不会被错误放大。

### 消融对照

对照只改变 FlowCell：路径和 NavTile 不变，局部人群执行网格改变。另一组只改变 GridCellSizeCm：世界 cell 数、烘焙目标和估算预算一起变化。

### 解释层

HUD 显示世界宽高、board cell、NavTile 数、FlowWindow、FlowCell 和估算内存；图例区分世界、烘焙、执行三种尺度。

### 旋钮清单

| 旋钮 | 范围 | 演示什么 |
|---|---|---|
| 目标世界宽度 | 场景合法米数 | 世界范围如何派生 |
| `GridCellSizeCm` | 配置预设 | 细化地形的代价 |
| `FlowCellSizeCm` | 可整除预设 | 执行层分辨率，不改 NavMesh |
| FlowWindow | 配置预设 | 局部执行范围，不覆盖整张地图 |

### 场景结构

主演示是同一地图的尺度推导；子场景分别展示 Grid、HexGrid 和 NodeGraph。首屏说明“先输入地图米数，再看各层尺度如何变化”。

### 门户资产

截图保留尺度表和预算变化；预览直接读取地图与 MassNavigation 配置，不复制数字。

### 反向 API 审计

需要 board 尺度派生、统一 estimate 输入摘要和编辑器 owner 标记。现有 `SpatialScaleDefaults`、BoardConfig 和 solver 校验可复用；跨模块联动尚未形成完整作者面。

## 5. 边界

本页不定义路径算法、地形语义或 MassNavigation 求解器；只定义尺度及其约束。大地图的流式加载另由产物和运行时预算负责。

## 6. UAT

```gherkin
Feature: 作者能区分导航体系中的空间尺度

  Scenario: 从目标米数派生地图
    Given 作者输入目标宽度、拓扑和 cell 尺寸
    When 作者保存 board
    Then 页面显示派生的 MacroTile、TerrainChunk 和 NavTile 数量
    And 世界宽度与派生结果一致

  Scenario: 执行分辨率不改变烘焙几何
    Given 一份已发布的 NavTile
    When 作者只调整 FlowCell
    Then NavTile build hash 不变
    And MassNavigation 执行网格显示新的分辨率
```

## 7. 现状与 TODO

空间尺度 SSOT、Grid/Hex/NodeGraph 的基础字段和 MassNavigation solver 校验已经存在；编辑器 meters-first 派生、所有 estimate 输入统一和跨层 owner 展示仍未收口。相关原始材料见 `architecture/spatial-scale-and-resolution-ssot.md` 与 `reference/map-scale-authoring-guide.md`。

| 责任层 | TODO | 完成判据 |
|---|---|---|
| 编辑器 | meters-first board 创建和尺度 owner 视图 | 保存后每层尺度可追溯 |
| 工具 | estimate 输出完整尺度摘要 | CLI/Bridge 结果一致 |
| Runtime | board/terrain/navmesh/flow 的尺度契约回归 | 非法组合明确失败 |
| Showcase | 大地图与局部执行窗口对照 | 玩家能说清两者区别 |
