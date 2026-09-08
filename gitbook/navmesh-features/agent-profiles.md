# Agent 半径与 Profile

> [功能目录](README.md) · [体系总合同](../navmesh-ssot.md)
>
> 第 1–6 节定义目标产品与设计验收；当前可用范围、分支成果和剩余工作只看第 7 节。本文不新增第二套数据或运行管线。

## 1. 概述

Agent Profile 只回答“这类单位的体型和身份是什么”。半径、身高、间隙、吃水、船宽、质量和 layer 是 profile 数据；坡度、台阶和净高的判定见 [坡度、台阶与净高](slope-clearance.md)。

证据入口：`src/Core/Navigation/AgentProfiles/AgentProfileConfig.cs`、`src/Core/Navigation/AgentProfiles/AgentProfileRegistry.cs`、`src/Core/Navigation/NavMesh/NavMeshProfileRegistry.cs`。

## 2. 结构

AgentProfileConfig 拥有 RadiusCm、HeightCm、ClearanceCm、DraftCm、BeamCm、Mass、Layer；NavMeshBakeConfig.Profiles 拥有同名 Id 与 MaxClimbCm、MaxSlopeDeg。当前按 Id 关联；目标是字段各有唯一来源，编辑器呈现为一个完整 profile 视图。

## 3. 详情

### 3.1 编辑器功能

- Profile 面板显示 id、半径、身高、clearance、draft、beam、mass 和 layer，并标注单位。
- 编辑器列出引用该 profile 的 board、layer 和实体类型；修改半径时显示受影响 tile。
- profile id、半径、身高和质量必须通过严格校验；没有默认 profile 可选。
- 路径模拟必须显式选择 profile，不能取“当前选中单位”的隐式值。

### 3.2 工具功能

- `AgentProfileRegistry` 与 `NavMeshProfileRegistry` 负责把 agent profile 和 bake profile 对齐。
- 当前 `NavMeshBakeConfig.Profiles` 只有 `Id`、`MaxClimbCm`、`MaxSlopeDeg`；其他体型字段仍来自 `AgentProfileConfig`，工具必须报告两者的组合关系。
- estimate、bake、manifest 和 query 使用同一 profile id；不同 profile 生成不同 build hash。
- profile 缺失、不匹配或单位非法时停止，不退回默认体型。

### 3.3 Runtime 功能

- query registry 按 layer/profile 选择对应 NavTileStore；实体体型与烘焙半径需追溯同一 profile。arrival radius 属于执行到达策略，单独声明。
- 已加载实体不会因为配置热改而静默换 profile；修改后需要重新绑定和重新烘焙。
- profile 只描述单位几何能力；区域 cost 由 AgentType 的 pathing 矩阵持有，障碍 footprint 与 Link 动作各由对应领域负责。

## 4. 场景与 Showcase 设计

### 一句话与目标用户

小兵走窄门，重装单位走宽路。

面向地图作者和首次体验该能力的玩家。空间能力：切换已烘焙的单位体型、选择不同宽度通道，观察通行能力和实际行走结果。

### 主循环

0–15 秒：让小兵去营地，走过窄门。15–35 秒：选择重装单位重复同一命令，看到它走外围宽路。35–60 秒：选择更窄的独立门道，再打开体型轮廓解释原因。惊喜时刻：同一目的地，两种体型各自到达，重装单位不会卡进窄门。

### 消融对照

并排选用只差半径的两种有效 profile，其余能力与区域代价相同：小体型穿窄门，大体型绕宽路。体型轮廓是辅助解释层；不把同一个大单位伪装为小半径。

### 解释层

小兵绿色、重装单位橙色；圆圈表示半径，虚线环表示安全间隙。HUD 显示 profileId、半径/直径（cm）、门宽（cm）、路径长度与到达人数。

### 旋钮清单

以下为目标演示中可在同一会话操作的控件；取值由场景配置或正式 API 约束。需要重烘焙的几何变更明确展示任务过程，不能伪装成立即生效。

| 运行中操作 | 范围 | 玩家能看懂什么 |
|---|---|---|
| 单位体型 | 场景已烘焙的 profile 列表 | 切换真实单位与匹配产物 |
| 通道目标 | 窄门 / 普通门 / 宽路 | 比较体型结果 |
| 出发位置 | 当前地图范围 | 观察绕行 |
| 队伍规模 | 场景配置的数量预设 | 比较可通过与拥挤的区别 |
| 体型轮廓 | 开 / 关 | 解释为什么绕路 |

### 场景结构

主演示：窄门加宽路；子场景：缺 profile、重烘焙后的新半径、产物不匹配。首屏：“先让小兵到营地，再派重装队伍去同一个位置。”

### 门户资产

以主循环的惊喜时刻截取真实画面，并保留操作前后状态。入口、配置和媒体由 `showcase.registry.json` 关联；预览消费场景配置与正式产物，禁止另写一份数值。尚未实现的媒体显示待制作，不挂不存在的图片链接。

### 反向 API 审计

| 所需接口 | 归属 | 现状与缺口 |
|---|---|---|
| 实体 profile / 产物匹配检查 | AgentProfiles / Query | 已有 Id 关联，待编辑器完整显示 |
| 半径修改的影响分析 | Tools | 待提供受影响瓦片和重烘焙清单 |
| 通道不足解释 | Query diagnostics | 需证据支持，普通 NoPath 不能擅自翻译为半径不足 |

这些缺口属于对应功能的后续实现范围；本次文档设计不实现 API。阻塞主循环的接口补齐前，不能把演示标为可玩。

### 交付边界与完成判据

本页 Showcase 状态：设计完成；所述完整演示尚未实现，不可玩。已有底层代码或零散场景不代表本页演示完成。 实现阶段必须提供真实 launcher/Mod/地图入口，按 [统一 Showcase 交付合同](showcase-delivery.md) 验证操作、消融、解释层和故障反馈；本页功能判据见第 6 节。

## 5. 边界

本页重点是半径、profile 引用与产物匹配。坡度/台阶/净高去 [几何约束](slope-clearance.md)，吃水去 [水面语义](water-semantics.md)。到达容差和人群间距是执行策略，不可当作烘焙半径替身。

## 6. UAT

```gherkin
Feature: Agent Profile 决定体型可行性

  Scenario: 小半径单位通过窄门
    Given 门洞宽度只允许 scout 通过
    When 玩家让 scout 和 heavy 查询同一目标
    Then scout 得到路径
    And heavy 显示通道不足

  Scenario: profile 缺失
    Given 地图没有声明请求的 profile
    When 玩家发起查询
    Then 系统显示 profile 不匹配
    And 不使用默认 profile
```

这些场景描述目标验收，不是已通过的测试记录。

## 7. 现状与 TODO

核对基线：2026-09-08，`origin/main 63afc7626f419acedcc4bd2f1ade33c8f6cd941f`。

主线已有 agent/profile registry、字段校验和按 Id 关联。AgentProfile 管体型、bake profile 管算法几何约束、Pathing AgentType 管区域代价，这种分工已经是现有合同，不是待合并的重复来源。剩余为编辑器影响分析、明确重绑定及各生产入口的请求身份接线与验收。

分支接收判断：主线已有两个 registry 与 Recast profile 输入；本次分支核查没有找到已完成配置面和全链验收的独立成果。

| 责任层 | 本页剩余功能点 | 完成判据 |
|---|---|---|
| 编辑器 | 集中显示 profile 字段及其唯一来源 | 作者能定位并修改本页数据，错误有具体位置 |
| 工具 | 半径修改影响分析、profile hash 和产物校验 | 相同输入可重复生成/验证，失败明确退出 |
| Runtime | 重绑定时原子切换 profile 与匹配产物 | 正式管线消费结果并报告失败，相关回归通过 |
| Showcase | 不同体型绕行、实际到达与缺产物失败 | 第 6 节行为通过，并满足统一运行验收要求 |

主线已有相关测试入口（证明已有合同覆盖范围；不表示本页目标 UAT 已执行）：

- `src/Tests/ArchitectureTests/NavMeshConfigContractTests.cs`
