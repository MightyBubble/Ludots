# NavMesh 真实调试显示

> [功能目录](README.md) · [体系总合同](../navmesh-ssot.md)
>
> 第 1–6 节定义目标产品与设计验收；当前可用范围、分支成果和剩余工作只看第 7 节。本文不新增第二套数据或运行管线。

## 1. 概述

调试显示把运行时实际加载的 NavTile、区域、边界和路径状态画出来，帮助作者区分“没有产物”“不可达”和“渲染层没显示”。它不生成新的几何，也不作为查询权威。

证据入口：`src/Core/Presentation/Navigation/NavMeshPresentationSystem.cs`、`src/Client/Ludots.Client.Raylib/Rendering/RaylibNavMeshPresentationRenderer.cs`。

## 2. 结构

NavTileStore → NavMeshPresentationBuffer/State/System → RaylibNavMeshPresentationRenderer。显示实际三角形、边界与路径版本；点击显示元素应回到其瓦片身份。

## 3. 详情

### 3.1 编辑器功能

- 面板可以按 board、layer、profile、tile revision 过滤。
- 显示模式包括可行面、area、border portal、dirty 状态和当前路径；每种模式有固定图例。
- 选择一个三角面可回到 tile identity、build hash 和 source revision。
- 没有对应 artifact 时显示“缺瓦片”，不画近似平面。

### 3.2 工具功能

- `NavMeshPresentationBuffer/State/System` 从 NavTileStore 派生渲染数据。
- Raylib renderer 只消费 presentation buffer；Web 预览使用同一 artifact 元数据，不复制 Detour 解析。
- 工具导出的截图或网格必须带 revision，防止把旧显示当成当前运行时状态。

### 3.3 Runtime 功能

- presentation state 与 query service 共享 `NavTileStore` 发布边界。
- Replace、Unload 和 revision 变化时刷新显示；旧三角形不能残留而误导用户。
- overlay 只解释状态，不改变 query、cost 或 MassNavigation 执行。

## 4. 场景与 Showcase 设计

### 一句话与目标用户

把路径下面真正的导航网格看清楚。

面向地图作者和首次体验该能力的玩家。空间显示：加载、替换、卸载瓦片时，真实几何和路径版本随之改变。

### 主循环

0–15 秒：按 N 打开网格并点选一块瓦片。15–35 秒：落门，看旧几何在新瓦片发布后替换。35–60 秒：切换区域与边界显示，读取同一条路径的版本。惊喜时刻是门前可行面缩回，单位路径同时绕行。

### 消融对照

N 开关隐藏/显示解释层，移动命令相同；显示开启后能说明为什么绕行。缺瓦片子场景直接显示空缺，不生成平面补位。

### 解释层

三角形按 area 着色、瓦片边界白线、脏瓦片黄色；HUD 显示选中瓦片、NavTile 版本、store 发布版本、查询版本和缺数据状态。

### 旋钮清单

以下为目标演示中可在同一会话操作的控件；取值由场景配置或正式 API 约束。需要重烘焙的几何变更明确展示任务过程，不能伪装成立即生效。

| 运行中操作 | 范围 | 玩家能看懂什么 |
|---|---|---|
| 网格开关 | 开 / 关 | 查看真实几何 |
| 显示模式 | 三角面 / area / border portal | 定位几何关系 |
| 选中瓦片 | 已加载范围 | 查看来源与版本 |
| profile/layer | 已加载组合 | 切换实际产物 |
| 脏区显示 | 开 / 关 | 跟踪更新范围 |

### 场景结构

主演示 NavGate 真实网格；子场景：缺瓦片、区域着色、发布后旧几何移除。首屏：“按 N，点选城门下的瓦片，再落门。”

### 门户资产

以主循环的惊喜时刻截取真实画面，并保留操作前后状态。入口、配置和媒体由 `showcase.registry.json` 关联；预览消费场景配置与正式产物，禁止另写一份数值。尚未实现的媒体显示待制作，不挂不存在的图片链接。

### 反向 API 审计

| 所需接口 | 归属 | 现状与缺口 |
|---|---|---|
| 运行时几何显示 | Core Presentation / Raylib | 主线已有 |
| 瓦片拾取与版本信息 | Presentation adapter | 待补交互读取 |
| Web 消费权威 artifact | Editor Bridge | 待收敛旧 payload |

这些缺口属于对应功能的后续实现范围；本次文档设计不实现 API。阻塞主循环的接口补齐前，不能把演示标为可玩。

### 交付边界与完成判据

本页 Showcase 状态：设计完成；所述完整演示尚未实现，不可玩。已有底层代码或零散场景不代表本页演示完成。 实现阶段必须提供真实 launcher/Mod/地图入口，按 [统一 Showcase 交付合同](showcase-delivery.md) 验证操作、消融、解释层和故障反馈；本页功能判据见第 6 节。

## 5. 边界

本页只管真实几何调试。投影 PNG 归 [投影贴图](walkability-projection.md)，失败码归 [查询诊断](query-diagnostics.md)。所有显示模式只读，不改变查询结果。

## 6. UAT

```gherkin
Feature: 调试显示与查询使用同一份 NavTile

  Scenario: 瓦片发布后显示同步
    Given 玩家打开 NavMesh overlay
    When 城门重烤结果发布
    Then overlay 显示新的 tile revision
    And 路径使用同一 revision

  Scenario: 缺瓦片可见
    Given 当前 layer 缺少目标瓦片
    When 玩家打开 overlay
    Then 画面标出缺瓦片区域
    And 不绘制伪造的可行面
```

这些场景描述目标验收，不是已通过的测试记录。

## 7. 现状与 TODO

核对基线：2026-09-08，`origin/main 9231f05fcf`。

主线已有 presentation buffer、Raylib renderer、N 开关和 overlay 合同测试；按 tile 拾取、过期显示和 Web/Raylib 同源预览仍需收口。

分支接收判断：真实 NavTile 显示已进入 main；历史 Web 展示分支仍需适配当前 Bridge，不整支回迁。

| 责任层 | 本页剩余功能点 | 完成判据 |
|---|---|---|
| 编辑器 | 瓦片拾取、版本信息、无数据状态 | 作者能定位并修改本页数据，错误有具体位置 |
| 工具 | 复用权威 artifact 导出显示数据 | 相同输入可重复生成/验证，失败明确退出 |
| Runtime | Replace/Unload 后清理旧几何 | 正式管线消费结果并报告失败，相关回归通过 |
| Showcase | 查询和显示版本同源验收 | 第 6 节行为通过，并满足统一运行验收要求 |

主线已有相关测试入口（证明已有合同覆盖范围；不表示本页目标 UAT 已执行）：

- `src/Tests/ArchitectureTests/NavMeshPresentationContractTests.cs`
- `src/Tests/RaylibAdapterTests/RaylibNavMeshPresentationContractTests.cs`
