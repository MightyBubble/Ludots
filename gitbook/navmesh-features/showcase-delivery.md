# NavMesh Showcase 交付

> [功能目录](README.md) · [体系总合同](../navmesh-ssot.md)
>
> 第 1–6 节定义目标产品与设计验收；当前可用范围、分支成果和剩余工作只看第 7 节。本文不新增第二套数据或运行管线。

## 1. 概述

NavMesh showcase 的目标是让没读过代码的新玩家在 60 秒内看懂：结构变化会让导航更新，单位会沿新路径继续走，系统还能解释原因。测试通过、注册表有条目或有静态截图，都不等于可玩交付。

## 2. 结构

以 NavGate 为总演示入口，各功能子场景跳转到对应单页。场景配置、启动 preset、showcase.registry.json 和验收资产共同指向同一真实 Mod/地图。

## 3. 详情

### 3.1 编辑器功能

- Showcase 配置从真实 map、board、profile、layer 和 runtime tier 引用，不复制一份地图参数。
- 编辑器可以预览主演示使用的 source、路径和 overlay，但不替代真实游戏运行。
- 设计稿必须标注每个 HUD 数值来自哪个 Runtime API。

### 3.2 工具功能

- launcher preset、`showcase.registry.json`、acceptance index、截图/录屏和 README 互相可达。
- 启动脚本只负责选择 preset 和记录证据，不在脚本里实现导航逻辑。
- 证据包含启动入口、操作序列、状态查询、日志和真实进程截图。

### 3.3 Runtime 功能

- Showcase 只调用正式 query、runtime rebake、presentation 和 MassNavigation API。
- 主循环至少暴露 dirty tile、revision、path status、arrival state 和失败原因。
- 消融开关必须改变正式 runtime 状态，例如冻结 rebake；装饰性按钮不算。

## 4. 场景与 Showcase 设计

### 一句话与目标用户

城门落下，小队绕路抵达营地。

面向地图作者和首次体验该能力的玩家。空间导航：玩家改变城门状态，看到更新范围、路径与整个队伍到达结果。

### 主循环

0–15 秒：首屏引导派兵去 B 营。15–35 秒：落门，显示待处理区域和处理状态。35–60 秒：发布新路并到达。复位后冻结重烘焙重试。惊喜时刻：整队一起转向绕路，成功以实际到达为准。

### 消融对照

正式 ProcessingEnabled 冻结/恢复；冻结时显示旧版本和等待状态，恢复后才出现新路。必须通过真实输入管线触发。

### 解释层

门关闭红色、脏瓦片黄色、更新路径青色、到达单位绿色；HUD 显示待处理数、发布版本、路径状态、耗时（ms）和到达人数。

### 旋钮清单

以下为目标演示中可在同一会话操作的控件；取值由场景配置或正式 API 约束。需要重烘焙的几何变更明确展示任务过程，不能伪装成立即生效。

| 运行中操作 | 范围 | 玩家能看懂什么 |
|---|---|---|
| 城门 | 升起 / 落下 | 触发结构变化 |
| 重烘焙 | 冻结 / 恢复 | 能力消融 |
| 目标营地 | 场景声明的营地 | 重新发出移动命令 |
| 队伍数量 | 场景合法预设 | 观察实际到达 |
| 解释层 | 网格 / 路径 / 隐藏 | 逐层理解结果 |

### 场景结构

主演示仅做 NavGate 城门绕行。多层、profile、水面、Link、贴图等分别进入功能子场景。首屏：“派兵去 B 营，落门看绕行；冻结重烘焙后再试一次。”

### 门户资产

以主循环的惊喜时刻截取真实画面，并保留操作前后状态。入口、配置和媒体由 `showcase.registry.json` 关联；预览消费场景配置与正式产物，禁止另写一份数值。尚未实现的媒体显示待制作，不挂不存在的图片链接。

### 反向 API 审计

| 所需接口 | 归属 | 现状与缺口 |
|---|---|---|
| 主循环状态 | Core rebake / Query / MassNavigation | 已有部分，待全队到达 |
| 正式操作与 HUD | Mod / UI / Presentation | 待四个以上有效运行时控件 |
| 运行证据 | Agent Bridge / Launcher | 待会话核对、操作、状态与截图 |

这些缺口属于对应功能的后续实现范围；本次文档设计不实现 API。阻塞主循环的接口补齐前，不能把演示标为可玩。

### 交付边界与完成判据

已实现，待运行验收。本页新增交互和子场景尚未证明可玩。 实现阶段必须提供真实 launcher/Mod/地图入口，按 [统一 Showcase 交付合同](showcase-delivery.md) 验证操作、消融、解释层和故障反馈；本页功能判据见第 6 节。

## 5. 边界

本页只定义统一交付方法与 NavGate 总体验，不重新定义各功能算法。注册表、设计稿、截图不能替代实际入口与操作；未实现的子场景显示计划状态，不伪装成可点击游玩。

## 6. UAT

```gherkin
Feature: 新玩家看懂 NavMesh 的动态更新

  Scenario: 城门落下并绕行
    Given 玩家已从门户启动 NavGate showcase
    And 小队正在从 A 营前往 B 营
    When 玩家按下城门操作键
    Then 玩家能看到 dirty tile 和重烤状态
    And 路径更新并绕开城门
    And 小队最终抵达 B 营

  Scenario: 运行时重烤消融
    Given 玩家已冻结 runtime rebuild
    When 玩家再次按下城门操作键
    Then HUD 明确显示重烤被冻结
    And revision 不增加
    And 玩家能看到旧路径没有被伪装成新结果
```

统一运行验收必须记录：真实启动 preset、两次 `/health` 且 `pumpCount` 增长、`session.info` 确认 Mod/地图、正式输入触发主循环与消融、故障反馈、操作前后状态/日志和目标进程截图。持久化子场景追加写入前、写入后、退出、重新读取、继续操作五个节点。相关 build、目标测试与本页行为验收均通过后，才标记“可玩交付完成”。

现有入口目录：`mods/showcases/navmesh_runtime_gate/NavGateShowcaseMod`；媒体：`artifacts/acceptance/navmesh_runtime_gate/screens/navgate_surprise_moment.png`。文件存在只说明有历史证据，不证明本次目标功能已验收。

这些场景描述目标验收，不是已通过的测试记录。

## 7. 现状与 TODO

核对基线：2026-09-08，`origin/main 63afc7626f419acedcc4bd2f1ade33c8f6cd941f`。

`navmesh_runtime_gate_showcase` 和 debug showcase 已登记，NavGate 的主循环已有实现；仓库已有部分截图和验收文件；当前仍缺同一目标会话的完整 Agent Bridge 观察→驱动→验证记录、HUD 完整性和所有子场景 UAT。状态只能写“已实现，待运行验收”。

主线已将 `ProcessingEnabled` 暴露为 F 键：`mods/showcases/navmesh_runtime_gate/NavGateShowcaseMod/Systems/NavGateTimelineSystem.cs` 处理 `NavGate_ToggleFreeze` 并切换队列；输入绑定见该 Mod 的 `assets/Input/default_input.json`。旧演示与合同测试仍以冻结后旧路径穿门作对照；本页目标要求过期路径在不安全路段前等待，这部分仍待实现/验收，不能按已有 F 键推定完成。

分支接收判断：NavGate 主线代码和入口存在，已实现，待运行验收；nav/bake-island-showcase 仅设计，#1402 的局部场景基础不足以证明完整交付。

| 责任层 | 本页剩余功能点 | 完成判据 |
|---|---|---|
| 编辑器 | 引用同源场景配置，显示未交付状态 | 作者能定位并修改本页数据，错误有具体位置 |
| 工具 | 登记真实入口、验收与媒体资产 | 相同输入可重复生成/验证，失败明确退出 |
| Runtime | 走正式下单、重烘焙、查询与移动链 | 正式管线消费结果并报告失败，相关回归通过 |
| Showcase | Agent Bridge 完成主循环、消融与故障验收 | 第 6 节行为通过，并满足统一运行验收要求 |

主线已有相关测试入口（证明已有合同覆盖范围；不表示本页目标 UAT 已执行）：

- `src/Tests/GasTests/Map/NavGateShowcaseContractTests.cs`
