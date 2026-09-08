# 每板寻址

> [功能目录](README.md) · [体系总合同](../navmesh-ssot.md)
>
> 第 1–6 节定义目标产品与设计验收；当前可用范围、分支成果和剩余工作只看第 7 节。本文不新增第二套数据或运行管线。

## 1. 概述

每板寻址让一张地图里的多个 board 各自拥有坐标原点、拓扑、瓦片尺寸、层和产物路径。它解决的是“同一坐标编号在不同 board 里不能互相覆盖”，不负责跨板寻路。

关联 issue：[#1346](https://github.com/MightyBubble/Ludots/issues/1346)。

## 2. 结构

地图拥有多个板（board）；每板声明原点、拓扑和瓦片宽高。完整寻址为 map → board → layer → profile → 局部 tile。世界坐标只在入口换算一次。

## 3. 详情

### 3.1 编辑器功能

- 地图面板先选择 `boardId`，再显示该板的拓扑、原点、宽高和瓦片范围。
- 网格和六边形板使用同一面板，但显示各自的两轴 tile 尺寸，不用 `ChunkWidthCm` 猜测。
- 加载、烘焙和查询结果都显示 `boardId`、局部 tile 坐标和世界坐标。
- 原点、拓扑或尺寸冲突直接显示错误；编辑器不得自动改成全局默认板。

### 3.2 工具功能

- CLI 和 Editor Bridge 从 board 声明解析 tile geometry、origin 和目标范围。
- manifest 的身份至少包含 `mapId/boardId/layerId/profileId/tileCoord`。
- 产物路径必须由 manifest 唯一推导；旧路径产物拒载并提示重新烘焙。
- grid+hex 混板、非零 origin 和同坐标 tile 覆盖必须有独立合同测试。

### 3.3 Runtime 功能

- `NavQueryServiceRegistry` 按 board、layer、profile 分配 `NavTileStore`。
- 世界坐标先减 board origin，再按该板两轴尺寸定位局部瓦片。
- board 内部可走路径由 NavMesh 完成；跨 board 连接交给更高层 board graph。
- `NavTileStore.Replace` 必须携带完整 board 身份，不能仅用 `(x,y,layer)`。

现有证据：`src/Core/Navigation/NavMesh/Config/NavTileGridConfig.cs`、`src/Core/Navigation/NavMesh/NavTileStore.cs`、`src/Core/Navigation/NavMesh/NavQueryServiceRegistry.cs`。

## 4. 场景与 Showcase 设计

### 一句话与目标用户

两块地图，同一编号也各走各的。

面向地图作者和首次体验该能力的玩家。空间能力：在大陆方格板与港口六边形板之间换起终点、关闭一块板上的门，观察局部坐标和另一块板的路径是否稳定。

### 主循环

0–15 秒：选择大陆和港口各一支队伍，分别点同局部编号的终点。15–35 秒：关闭大陆门，观察只在大陆出现脏瓦片。35–60 秒：切换到港口继续行军，再开门复位。惊喜时刻：两板都显示局部瓦片 (0,0)，港口路径却完全不受大陆变化影响。操作确认在 1 秒内可见；烘焙等待显示进度。

### 消融对照

同一双板场景对比完整板身份的产物与隔离的缺身份测试副本：完整身份时两板独立查询，缺身份副本明确拒载并指出冲突的 (0,0)。正式管线不关闭板校验，不制造串板成功；“显示板身份”只是辅助解释控件。

### 解释层

蓝色边框代表大陆、橙色边框代表港口；HUD 显示 boardId、世界坐标/局部坐标（cm）、瓦片宽高（cm）、发布版本和路径长度（cm）。

### 旋钮清单

以下为目标演示中可在同一会话操作的控件；取值由场景配置或正式 API 约束。需要重烘焙的几何变更明确展示任务过程，不能伪装成立即生效。

| 运行中操作 | 范围 | 玩家能看懂什么 |
|---|---|---|
| 选中板 | 大陆 / 港口 | 切换真实查询上下文 |
| 起点位置 | 当前板可选范围 | 改变进入的局部瓦片 |
| 目标位置 | 当前板可选范围 | 观察边界换算 |
| 大陆门 | 开启 / 关闭 | 验证更新只影响大陆 |
| 显示板身份 | 开 / 关 | 比较能否识别同编号瓦片 |

### 场景结构

主演示：大陆与港口同时行军。子场景：非零原点、同局部编号、板身份缺失。首屏：“两边各点一次终点，再关闭大陆门，看港口是否受影响。”

### 门户资产

以主循环的惊喜时刻截取真实画面，并保留操作前后状态。入口、配置和媒体由 `showcase.registry.json` 关联；预览消费场景配置与正式产物，禁止另写一份数值。尚未实现的媒体显示待制作，不挂不存在的图片链接。

### 反向 API 审计

| 所需接口 | 归属 | 现状与缺口 |
|---|---|---|
| 板内寻址与带 board 的 registry | Core Navigation | 现 registry key 只有 layer/profile，需补完整板身份 |
| 板身份和路径诊断 | Tools / Query | 需补混板冷加载和拒载信息 |

这些缺口属于对应功能的后续实现范围；本次文档设计不实现 API。阻塞主循环的接口补齐前，不能把演示标为可玩。

### 交付边界与完成判据

本页 Showcase 状态：设计完成；所述完整演示尚未实现，不可玩。已有底层代码或零散场景不代表本页演示完成。 实现阶段必须提供真实 launcher/Mod/地图入口，按 [统一 Showcase 交付合同](showcase-delivery.md) 验证操作、消融、解释层和故障反馈；本页功能判据见第 6 节。

## 5. 边界

本页管板内坐标和产物隔离。跨板路线组合交给路径路由；跨表面动作见 [NavMesh Link](navmesh-links.md)。调整板原点或瓦片尺寸必须重新生成产物，不能作为即时查询旋钮。

## 6. UAT

```gherkin
Feature: 多板地图保持独立寻址

  Scenario: grid 和 hex board 同时加载
    Given 地图包含不同原点的 grid board 和 hex board
    When 作者分别烘焙并启动地图
    Then 两块 board 的 tile identity 和产物路径不同
    And 在一块 board 上替换瓦片不会改变另一块 board

  Scenario: 非零 origin 查询
    Given 玩家在非零 origin 的 board 上选择起点和终点
    When 玩家执行寻路
    Then 查询使用该 board 的局部坐标
    And HUD 显示正确的 board 和 tile 坐标
```

这些场景描述目标验收，不是已通过的测试记录。

## 7. 现状与 TODO

核对基线：2026-09-08，`origin/main 9231f05fcf`。

`NavTileGrid` 声明已进入 main；完整 board registry、manifest 路径、非零 origin 的真实加载和混板 UAT 仍未收口。#1402 的 origin 修复只能作为局部候选，不能替代本页合同。

分支接收判断：#1362 的每板声明已在主线；#1402 的 origin 候选仅覆盖局部查询，不能代替混板合同。

| 责任层 | 本页剩余功能点 | 完成判据 |
|---|---|---|
| 编辑器 | 板选择与原点/局部坐标检查面板 | 作者能定位并修改本页数据，错误有具体位置 |
| 工具 | 板身份产物路径和混板 manifest | 相同输入可重复生成/验证，失败明确退出 |
| Runtime | registry 加 board 身份并统一原点换算 | 正式管线消费结果并报告失败，相关回归通过 |
| Showcase | 双板隔离、非零原点和冷启动验收 | 第 6 节行为通过，并满足统一运行验收要求 |

主线已有相关测试入口（证明已有合同覆盖范围；不表示本页目标 UAT 已执行）：

- `src/Tests/ArchitectureTests/NavQueryRuntimeNonZeroTileTests.cs`
