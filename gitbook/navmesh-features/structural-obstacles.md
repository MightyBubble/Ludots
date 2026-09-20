# 结构障碍足迹

> [功能目录](README.md) · [体系总合同](../navmesh-ssot.md)
>
> 第 1–6 节定义目标产品与设计验收；当前可用范围、分支成果和剩余工作只看第 7 节。本文不新增第二套数据或运行管线。

## 1. 概述

结构障碍是会改变远程连通性的建筑、墙、门、桥基座等持久对象。本页定义它们怎样成为导航输入；移动单位的临时避让仍归 MassNavigation，不在这里建立另一套障碍系统。

证据入口：`src/Core/Ludots.Physics2D/Navigation/NavObstacleAuthoringAdapter.cs`、`src/Core/Navigation/NavMesh/Config/INavObstacleAuthoringProvider.cs`。

## 2. 结构

地图对象的稳定身份、board/layer、几何足迹和启用状态 → INavObstacleAuthoringProvider → NavObstacleSet。障碍模型负责输入；dirty system 比较变化并将旧、新区域交给重烘焙。

## 3. 详情

### 3.1 编辑器功能

- 障碍面板展示稳定 id、board、layer、footprint、启用状态和 source revision。
- 移动或缩放障碍时，同时显示旧足迹、新足迹及其并集 dirty AABB。
- 装饰模型没有结构标签时，编辑器明确提示“不会阻挡导航”。
- 删除障碍需要显示哪些 tile 将恢复可走；不能只删视觉对象。

### 3.2 工具功能

- `INavObstacleAuthoringProvider` 将作者对象转成 `NavObstacleSet`，与 mesh/height source 一起生成 snapshot。
- footprint、层和生效时间进入 hash；重复 id、无效几何和跨 board 足迹直接失败。
- 工具只负责输入和验证，dirty 计算由 Runtime dirty system 统一完成。

### 3.3 Runtime 功能

- `RuntimeNavMeshObstacleDirtySystem` 接收结构变化，发出 board-local dirty AABB。
- 旧足迹和新足迹都必须进入 dirty set；发布顺序由 [Runtime 脏烘](runtime-dirty-rebake.md) 负责。
- 障碍实体的移动不直接修改 NavTile，也不让查询读到半更新几何。

## 4. 场景与 Showcase 设计

### 一句话与目标用户

把墙挪走，旧路通了，新路堵了。

面向地图作者和首次体验该能力的玩家。空间能力：放置、移动、删除结构障碍，观察旧位置与新位置同时更新。

### 主循环

0–15 秒：放一段墙在主路。15–35 秒：把墙移到旁路，旧路恢复、新路阻断。35–60 秒：删除墙，再移动无导航标签的装饰塔。惊喜时刻：移墙一次改变两个位置，而移动装饰塔不会触发重烘焙。

### 消融对照

并排使用相同外形的结构墙与装饰模型，明确标记语义差异。结构墙影响路径，装饰模型只改画面；不热改生产对象标签假装配置自动生效。

### 解释层

旧足迹虚线、新足迹实线、受影响区域半透明；HUD 显示对象身份、作用层、足迹尺寸（cm）、障碍状态和瓦片版本。

### 旋钮清单

以下为目标演示中可在同一会话操作的控件；取值由场景配置或正式 API 约束。需要重烘焙的几何变更明确展示任务过程，不能伪装成立即生效。

| 运行中操作 | 范围 | 玩家能看懂什么 |
|---|---|---|
| 对象种类 | 结构墙 / 门 / 装饰 | 区分几何与导航语义 |
| 对象位置 | 配置允许的放置范围 | 同时影响旧/新位置 |
| 门状态 | 打开 / 关闭 | 切换生效状态 |
| 对象朝向 | 场景旋转预设 | 改变真实足迹 |
| 删除对象 | 选中对象 | 验证旧区域恢复 |

### 场景结构

主演示移墙；子场景：旋转墙、删除恢复、重复身份拒绝。首屏：“把墙从主路拖到旁路，观察两条路交换通行状态。”

### 门户资产

以主循环的惊喜时刻截取真实画面，并保留操作前后状态。入口、配置和媒体由 `showcase.registry.json` 关联；预览消费场景配置与正式产物，禁止另写一份数值。尚未实现的媒体显示待制作，不挂不存在的图片链接。

### 反向 API 审计

| 所需接口 | 归属 | 现状与缺口 |
|---|---|---|
| 足迹与稳定身份读取 | Authoring adapter | 已有 provider/adapter，待诊断输出 |
| 旧新区域变更 | Physics2D / Core dirty | 需移动、旋转、删除覆盖 |
| 足迹显示 | Presentation | 读取正式 NavObstacleSet |

这些缺口属于对应功能的后续实现范围；本次文档设计不实现 API。阻塞主循环的接口补齐前，不能把演示标为可玩。

### 交付边界与完成判据

本页 Showcase 状态：设计完成；所述完整演示尚未实现，不可玩。已有底层代码或零散场景不代表本页演示完成。 实现阶段必须提供真实 launcher/Mod/地图入口，按 [统一 Showcase 交付合同](showcase-delivery.md) 验证操作、消融、解释层和故障反馈；本页功能判据见第 6 节。

## 5. 边界

本页只管结构障碍输入。装饰、物理碰撞和导航障碍有各自语义，不能自动互推；移动的单位不是持久建筑。跨板对象需明确按板切分规则，不用平面包围盒猜归属。

## 6. UAT

```gherkin
Feature: 只有结构障碍改变导航连通性

  Scenario: 城门足迹进入 dirty set
    Given 城门有稳定 id 和结构障碍标签
    When 玩家把城门从打开变为关闭
    Then 旧足迹和新足迹都被标记
    And 受影响瓦片进入重烤队列

  Scenario: 装饰模型不阻挡路径
    Given 塔模型没有结构障碍标签
    When 玩家移动塔模型
    Then 路径和 NavTile revision 不变
    And 编辑器显示该对象未接入导航
```

这些场景描述目标验收，不是已通过的测试记录。

## 7. 现状与 TODO

核对基线：2026-09-08，`origin/main 63afc7626f419acedcc4bd2f1ade33c8f6cd941f`。

主线已有障碍 authoring adapter、dirty system 和合同测试；结构对象的稳定资产模型、足迹预览和移动障碍 UAT 仍需产品化。不能把任意碰撞模型自动当成 NavMesh 障碍。

分支接收判断：codex/nav-bake-policy 有 authored obstacle 输入片段；优先接入现有 provider，不再建编辑器障碍表。

| 责任层 | 本页剩余功能点 | 完成判据 |
|---|---|---|
| 编辑器 | 足迹、作用层与稳定身份可视编辑 | 作者能定位并修改本页数据，错误有具体位置 |
| 工具 | 重复身份、无效几何及板归属校验 | 相同输入可重复生成/验证，失败明确退出 |
| Runtime | 移动/旋转/删除同时标记旧新区域 | 正式管线消费结果并报告失败，相关回归通过 |
| Showcase | 结构墙与装饰对照、旧路恢复 | 第 6 节行为通过，并满足统一运行验收要求 |

主线已有相关测试入口（证明已有合同覆盖范围；不表示本页目标 UAT 已执行）：

- `src/Tests/ArchitectureTests/NavigationObstacleAuthoringContractTests.cs`
