# 投影可行性贴图

> [功能目录](README.md) · [体系总合同](../navmesh-ssot.md)
>
> 第 1–6 节定义目标产品与设计验收；当前可用范围、分支成果和剩余工作只看第 7 节。本文不新增第二套数据或运行管线。

## 1. 概述

投影可行性贴图把 NavMesh 的真实可走、不可走、area 和障碍状态投影到地表，服务作者检查和玩家理解。它是 presentation，不是第二份导航数据。

## 2. 结构

已发布 NavTile → 按 board/layer/profile 投影 → PNG/纹理与元数据 → 编辑器或运行时叠加。纹理记录来源版本；查询继续使用 NavTile。

## 3. 详情

### 3.1 编辑器功能

- 编辑器可切换 walkable、blocked、area、dirty 和 revision 图层。
- 贴图与当前 board/layer/profile 对齐，显示 source revision 和 tile revision。
- 选中像素或 tile 时显示对应 area、障碍来源和失败原因。
- 没有权威产物时显示“无数据”，不画猜测的绿色面。

### 3.2 工具功能

- `src/Tools/Ludots.Tool/WalkabilityTextureExporter.cs` 从正式 bake 结果导出走性纹理。
- 导出命令记录 map、board、layer、profile、source revision 和 build hash。
- PNG 是诊断/展示资产，不参与 Runtime query；`.ntil` 仍是唯一权威产物。
- 导出失败必须指出缺失 tile、格式或版本不匹配。

### 3.3 Runtime 功能

- `NavMeshPresentationBuffer`、`NavMeshPresentationState` 和 presentation system 从 NavTile 产物派生显示数据。
- overlay 的 tile、area 和 revision 必须与 query 使用同一 store。
- 结构变更时标出待更新范围；NavTile 发布后旧纹理标为过期，直到派生图刷新。界面同时显示查询与纹理来源版本，不能宣称异步图像瞬间更新。

## 4. 场景与 Showcase 设计

### 一句话与目标用户

看到能走的面，也能看出图何时过期。

面向地图作者和首次体验该能力的玩家。显示能力：切换 profile、放置结构障碍，观察投影范围和图像版本变化。

### 主循环

0–15 秒：选小兵和重装的投影比较窄路。15–35 秒：落门，旧图立即标出过期范围。35–60 秒：刷新投影再查询，路径与最新图对应。惊喜时刻：一片绿色孤岛仍然不可达，界面解释绿色只代表局部可行面。

### 消融对照

开/关投影叠加，保留同一查询结果；另可暂停派生图刷新，此时必须显示旧版本和过期标签，不能让旧绿面看起来是新结果。

### 解释层

绿色可行、红色阻挡、灰色无数据、黄色过期；HUD 显示板/层/profile、投影范围（cm）、像素分辨率、build hash 和源瓦片版本。

### 旋钮清单

以下为目标演示中可在同一会话操作的控件；取值由场景配置或正式 API 约束。需要重烘焙的几何变更明确展示任务过程，不能伪装成立即生效。

| 运行中操作 | 范围 | 玩家能看懂什么 |
|---|---|---|
| 投影显示 | 开 / 关 | 消融解释层 |
| 透明度 | 0–100% | 比较地表和投影 |
| profile | 已烘焙体型列表 | 观察可行面差异 |
| 图层 | 可行性 / 区域 / 过期范围 | 解释图像含义 |
| 派生图刷新 | 暂停 / 恢复 | 验证过期状态 |

### 场景结构

主演示地表投影；子场景：孤岛不可达、动态门、上下表面分别投影。首屏：“打开投影，给绿色孤岛下达命令，看查询如何解释。”

### 门户资产

以主循环的惊喜时刻截取真实画面，并保留操作前后状态。入口、配置和媒体由 `showcase.registry.json` 关联；预览消费场景配置与正式产物，禁止另写一份数值。尚未实现的媒体显示待制作，不挂不存在的图片链接。

### 反向 API 审计

| 所需接口 | 归属 | 现状与缺口 |
|---|---|---|
| 投影导出及元数据 | Tools exporter | 已有 PNG，待完整版本与身份元数据 |
| 派生图失效通知 | Presentation | 待连接 NavTile 发布 |
| 表面过滤 | Presentation / Source | 依赖多表面能力 |

这些缺口属于对应功能的后续实现范围；本次文档设计不实现 API。阻塞主循环的接口补齐前，不能把演示标为可玩。

### 交付边界与完成判据

本页 Showcase 状态：设计完成；所述完整演示尚未实现，不可玩。已有底层代码或零散场景不代表本页演示完成。 实现阶段必须提供真实 launcher/Mod/地图入口，按 [统一 Showcase 交付合同](showcase-delivery.md) 验证操作、消融、解释层和故障反馈；本页功能判据见第 6 节。

## 5. 边界

本页管地表投影图像；三角网格和 portal 线框去 [真实调试显示](navmesh-debug-view.md)。颜色表示局部可行区域，不证明两点连通；重叠高度必须分表面显示，不能压成一张权威图。

## 6. UAT

```gherkin
Feature: 可行性贴图反映真实 NavMesh

  Scenario: 查询与贴图使用同一 revision
    Given 地图已加载一个带 revision 的 NavTile
    When 玩家打开 walkability overlay 并查询路径
    Then overlay 和路径显示相同 tile revision
    And 选中区域能看到 area 和可走状态

  Scenario: 动态障碍更新贴图
    Given 玩家让建筑进入地图
    When runtime rebake 发布新 tile
    Then 旧贴图显示过期标签和对应范围
    And 刷新后显示新 walkability 状态及来源版本
```

这些场景描述目标验收，不是已通过的测试记录。

## 7. 现状与 TODO

核对基线：2026-09-08，`origin/main 63afc7626f419acedcc4bd2f1ade33c8f6cd941f`。

走性 PNG 导出、NavTile overlay 和生产帧 NavMesh presentation 已在 main。编辑器统一入口、`.ntil` 单 payload 和冷启动后显示/查询一致性仍需完成。

分支接收判断：主线已有 PNG 导出和 overlay descriptor；nav-bake-visualization 的旧 payload 只能抽取适配，不作为新的查询数据源。

| 责任层 | 本页剩余功能点 | 完成判据 |
|---|---|---|
| 编辑器 | 投影模式、图例、源身份和过期提示 | 作者能定位并修改本页数据，错误有具体位置 |
| 工具 | 导出纹理与同源元数据 | 相同输入可重复生成/验证，失败明确退出 |
| Runtime | 发布后使旧图过期并刷新派生数据 | 正式管线消费结果并报告失败，相关回归通过 |
| Showcase | 孤岛与动态更新、缺数据区别验收 | 第 6 节行为通过，并满足统一运行验收要求 |

主线已有相关测试入口（证明已有合同覆盖范围；不表示本页目标 UAT 已执行）：

- `src/Tests/ArchitectureTests/WalkabilityTextureExporterTests.cs`
- `src/Tests/RaylibAdapterTests/NavWalkabilityOverlayDescriptorTests.cs`
