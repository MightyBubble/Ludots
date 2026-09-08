# Runtime 脏烘

> [功能目录](README.md) · [体系总合同](../navmesh-ssot.md)
>
> 第 1–6 节定义目标产品与设计验收；当前可用范围、分支成果和剩余工作只看第 7 节。本文不新增第二套数据或运行管线。

## 1. 概述

脏烘发现并处理结构障碍变化影响的瓦片。它负责“哪些地方需要重烤”，不负责预算/代次细节（见 [Runtime 烘焙预算与版本](runtime-bake-budget.md)），也不负责障碍足迹建模（见 [结构障碍足迹](structural-obstacles.md)）。

关联 issue：[#1347](https://github.com/MightyBubble/Ludots/issues/1347)。

## 2. 结构

结构变化 → 旧/新足迹并集 → 板内脏瓦片集合 → 提交重烘焙 → 发布版本 → 刷新路径。dirty set 的唯一计算由 Core 提供，编辑器和工具只预览。

## 3. 详情

### 3.1 编辑器功能

- 地图视图显示 dirty AABB、board-local tile、邻居扩展、队列状态和当前 revision。
- 作者可选择当前瓦片、当前瓦片加邻居或完整重烤，界面列出真实瓦片数量。
- 重烤失败显示阶段、原因和动作；旧路径不会被标成新结果。

### 3.2 工具功能

- 离线 bake 生成初始 `.ntil`；runtime queue 复用 Core bake service，不复制命令行循环。
- 工具调用 Core dirty planner，根据 obstacle footprint、board origin 和 tile geometry 预览 dirty set。
- source、profile、layer、semantic 或 board 缺失时 fail-fast，不用默认配置替代。

### 3.3 Runtime 功能

正式流程：

```text
结构障碍变化 → dirty AABB → board-local targets → snapshot
→ bounded bake → generation check → NavTileStore.Replace
→ cache invalidation → path refresh
```

- 旧足迹和新足迹都进入 dirty set。
- worker 不直接写 store；主线程只发布完整结果。
- revision 只在发布成功后推进；失败保留原因。

证据：`src/Core/Navigation/NavMesh/Bake/RuntimeIncrementalNavMeshRebuildQueue.cs`、`src/Core/Ludots.Physics2D/Systems/RuntimeNavMeshObstacleDirtySystem.cs`。

## 4. 场景与 Showcase 设计

### 一句话与目标用户

门刚落下，小队找到另一条路。

面向地图作者和首次体验该能力的玩家。空间能力：门或建筑改变连通性，脏区域、发布版本、路径与实际到达随之改变。

### 主循环

0–15 秒：小队向 B 营行军。15–35 秒：按“落门”，1 秒内标出待更新区域，继续显示处理状态。35–60 秒：瓦片发布，路径改色绕行，小队到达后升门复位。惊喜时刻是整队在门前转向外围道路；成功必须包括所有队员到达。

### 消融对照

冻结/恢复正式重烘焙队列。同一落门操作下，冻结侧标明导航过期，单位在危险路段前等待；恢复侧发布后绕行。冻结不关闭物理障碍，也不把旧路径称为新路径。

### 解释层

黄色瓦片待处理，紫色正在烘焙，绿色为最新可行面；HUD 显示待处理数、瓦片版本、路径状态、重算耗时（ms）、到达人数/总人数。

### 旋钮清单

以下为目标演示中可在同一会话操作的控件；取值由场景配置或正式 API 约束。需要重烘焙的几何变更明确展示任务过程，不能伪装成立即生效。

| 运行中操作 | 范围 | 玩家能看懂什么 |
|---|---|---|
| 城门 | 打开 / 关闭 | 改变连通性 |
| 队列处理 | 冻结 / 恢复 | 消融局部更新 |
| 障碍位置 | 场景允许的放置区 | 改变脏瓦片范围 |
| 行军终点 | A 营 / B 营 / 外围营地 | 验证路径刷新 |
| 更新范围显示 | 开 / 关 | 看清局部更新边界 |

### 场景结构

主演示 NavGate；子场景：移除障碍恢复、移到相邻瓦片、完全封路。首屏：“派小队去 B 营，落下城门，看它们能否绕路到达。”

### 门户资产

以主循环的惊喜时刻截取真实画面，并保留操作前后状态。入口、配置和媒体由 `showcase.registry.json` 关联；预览消费场景配置与正式产物，禁止另写一份数值。尚未实现的媒体显示待制作，不挂不存在的图片链接。

### 反向 API 审计

| 所需接口 | 归属 | 现状与缺口 |
|---|---|---|
| 脏区与队列状态 | Core Navigation / Physics2D adapter | 已有队列与 dirty system，待完整板身份 |
| 版本通知和路径刷新 | Query / MassNavigation | 待全队到达回归 |
| 门前等待 | MassNavigation / 碰撞执行 | 必须验证过期路径不会穿门 |

这些缺口属于对应功能的后续实现范围；本次文档设计不实现 API。阻塞主循环的接口补齐前，不能把演示标为可玩。

### 交付边界与完成判据

已有 NavGate 实现，待运行验收。本页新增交互和子场景尚未证明可玩。 实现阶段必须提供真实 launcher/Mod/地图入口，按 [统一 Showcase 交付合同](showcase-delivery.md) 验证操作、消融、解释层和故障反馈；本页功能判据见第 6 节。

## 5. 边界

只处理持久结构引起的局部更新。worker、代次和预算归 [Runtime 预算](runtime-bake-budget.md)；临时人群拥挤归移动避让。旧几何仅作为带过期标记的完整快照，不保证仍安全可走，执行层必须防止穿过已关闭的门。

## 6. UAT

```gherkin
Feature: 结构变化触发局部重烤

  Scenario: 城门落下后队伍绕行
    Given 队伍正在从 A 营前往 B 营
    When 玩家让城门落下
    Then 受影响瓦片显示 dirty
    And 新 revision 发布
    And 新路径绕开城门
    And 队伍最终抵达 B 营

  Scenario: 冻结重烤
    Given 玩家已冻结 runtime rebuild
    When 玩家让城门落下
    Then revision 保持不变
    And HUD 显示重烤被冻结
    And 系统不伪造新路径
```

这些场景描述目标验收，不是已通过的测试记录。

## 7. 现状与 TODO

核对基线：2026-09-08，`origin/main 63afc7626f419acedcc4bd2f1ade33c8f6cd941f`。

主线已有后台队列、脏区收集、主线程发布和 NavGate；运行时完整到达验收、source snapshot、budget/generation 合同和 #1164 的缓存/worker 仍未在 main 收口。状态只能写“已实现，待运行验收”。

分支接收判断：主线已有后台重烘焙；#1164 未合主线。此前审计记录存在版本已更新但全队未到达，不能按版本变化判定交付。

| 责任层 | 本页剩余功能点 | 完成判据 |
|---|---|---|
| 编辑器 | 旧/新足迹与脏瓦片预览 | 作者能定位并修改本页数据，错误有具体位置 |
| 工具 | 调用同一 dirty planner，展示实际目标数量 | 相同输入可重复生成/验证，失败明确退出 |
| Runtime | 发布通知到路径刷新与过期路径处理 | 正式管线消费结果并报告失败，相关回归通过 |
| Showcase | 复验 NavGate 全队到达和冻结对照 | 第 6 节行为通过，并满足统一运行验收要求 |

主线已有相关测试入口（证明已有合同覆盖范围；不表示本页目标 UAT 已执行）：

- `src/Tests/GasTests/Map/NavGateShowcaseContractTests.cs`
