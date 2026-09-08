# 烘焙源接入

> [功能目录](README.md) · [体系总合同](../navmesh-ssot.md)
>
> 第 1–6 节定义目标产品与设计验收；当前可用范围、分支成果和剩余工作只看第 7 节。本文不新增第二套数据或运行管线。

## 1. 概述

烘焙源接入只定义高度、网格和六边形数据如何进入 NavBakeContext，并带有稳定 URI、revision 和 board 归属。区域代价见 [区域语义与路径代价](area-costs.md)，水面规则见 [水面导航语义](water-semantics.md)。

关联 issue：[#1342](https://github.com/MightyBubble/Ludots/issues/1342)、[#1356](https://github.com/MightyBubble/Ludots/issues/1356)。

## 2. 结构

地图板声明源类型、URI、范围和版本；源适配器读取 .height/.grid/.hex，形成 NavBakeContext 可消费的快照。语义和障碍通过各自输入合同与该快照对齐。

## 3. 详情

### 3.1 编辑器功能

- Source 面板显示 source kind、URI、revision、board 覆盖范围和快照状态。
- `.height`、`.grid`、`.hex` 的坐标系和单位明确显示；重复来源或覆盖冲突直接阻止 bake。
- 作者能看到哪些修改只影响 runtime dirty set，哪些修改必须重建离线 artifact。

### 3.2 工具功能

- source adapter 把连续高度、grid 和 hex 输入转换成同一 Core snapshot。
- CLI、Editor Bridge 和 Runtime 只能消费 `NavBakeContext` 的同一 source contract。
- estimate、bake 和 manifest 使用同一 source revision/hash；不支持的组合直接失败。

证据入口：`src/Core/Navigation/NavMesh/Bake/NavBakeContext.cs`、`src/Core/Navigation/NavMesh/Bake/NavBakeService.cs`、`src/Core/Navigation/Terrain/LogicTerrainField.cs`。

### 3.3 Runtime 功能

- runtime rebake 开始前冻结 source snapshot；查询只读取已发布 artifact。
- source revision 不匹配时拒载并提示重新烘焙。
- source adapter 不负责猜 area、water 或 obstacle 语义，避免职责混合。

## 4. 场景与 Showcase 设计

### 一句话与目标用户

改一份地形，编辑器和烘焙工具同步理解。

面向地图作者和首次体验该能力的玩家。管线能力：修改高度源一个区域，经统一适配器传播到预览、estimate、产物和查询。

### 主循环

0–15 秒：选已声明的高度源查看范围。15–35 秒：在工作副本修改一处坡面并提交估算。35–60 秒：烘焙后加载，比较两个入口的源指纹和几何。惊喜时刻：编辑器和 CLI 对同一修订生成一致结果，缺源时直接指出 URI。

### 消融对照

对比“尚未发布的源工作副本”和“已烘焙发布修订”，两侧明确版本。开启发布后查询才采用新源；严格校验始终开启。

### 解释层

源覆盖范围蓝框、修改区域橙框；HUD 显示源类型、URI、源指纹、坐标单位、估算瓦片数和生成状态。

### 旋钮清单

以下为目标演示中可在同一会话操作的控件；取值由场景配置或正式 API 约束。需要重烘焙的几何变更明确展示任务过程，不能伪装成立即生效。

| 运行中操作 | 范围 | 玩家能看懂什么 |
|---|---|---|
| 源场景 | 已声明的 height / grid / hex 样例 | 比较适配入口 |
| 局部修改范围 | 场景允许范围 | 观察受影响瓦片 |
| 修改幅度 | 源工具合法高度预设 | 改变真实几何 |
| 查看修订 | 工作副本 / 已发布 | 比较修改传播 |
| 操作入口 | 编辑器 / CLI 面板 | 验证同源结果 |

### 场景结构

主演示连续高度源编辑；子场景：grid、hex、缺源、重复来源。首屏：“改一处坡面，观察估算范围，再从两个入口生成结果。”

### 门户资产

以主循环的惊喜时刻截取真实画面，并保留操作前后状态。入口、配置和媒体由 `showcase.registry.json` 关联；预览消费场景配置与正式产物，禁止另写一份数值。尚未实现的媒体显示待制作，不挂不存在的图片链接。

### 反向 API 审计

| 所需接口 | 归属 | 现状与缺口 |
|---|---|---|
| 源适配与能力声明 | NavBake Core | 独立 NavBakeSource、grid/hex 待收敛 |
| 快照/源指纹 | Core source | 待不可变合同 |
| 错误定位 | Tools / Bridge | 复用 Core 校验，待作者提示 |

这些缺口属于对应功能的后续实现范围；本次文档设计不实现 API。阻塞主循环的接口补齐前，不能把演示标为可玩。

### 交付边界与完成判据

本页 Showcase 状态：设计完成；所述完整演示尚未实现，不可玩。已有底层代码或零散场景不代表本页演示完成。 实现阶段必须提供真实 launcher/Mod/地图入口，按 [统一 Showcase 交付合同](showcase-delivery.md) 验证操作、消融、解释层和故障反馈；本页功能判据见第 6 节。

## 5. 边界

源接入只读几何，不解释区域代价、水深或障碍。三种源不要求相互产生完全相同的几何；同一输入经 CLI 与编辑器应一致。source revision 来源于真实内容，不能供用户任意输入数字。

## 6. UAT

```gherkin
Feature: 所有入口消费同一烘焙源

  Scenario: CLI 与编辑器结果一致
    Given 作者使用同一 source、board、profile 和 layer
    When 作者分别从 CLI 和 Editor Bridge 执行 bake
    Then 两边的 build hash 相同
    And 对应 NavTile identity 相同

  Scenario: source 冲突被拒绝
    Given 两个源声明覆盖同一 board 区域
    When 作者执行 bake
    Then 工具显示冲突字段
    And 不生成替代产物
```

这些场景描述目标验收，不是已通过的测试记录。

## 7. 现状与 TODO

核对基线：2026-09-08，`origin/main 63afc7626f419acedcc4bd2f1ade33c8f6cd941f`。

命名迁移、连续高度图和严格 `NavBakeContext` 已在 main；独立 `NavBakeSource`、grid/hex adapter、完整 snapshot 和 policy 仍未收口。`codex/nav-bake-policy` 只能提取实现片段，不能整支合入。

分支接收判断：codex/nav-bake-policy 有 source/policy 实现片段；命名迁移和高度场直灌已经在 main，不能重复实现。

| 责任层 | 本页剩余功能点 | 完成判据 |
|---|---|---|
| 编辑器 | 源 URI、范围和快照状态面板 | 作者能定位并修改本页数据，错误有具体位置 |
| 工具 | 提取 source/policy 并统一 CLI/Bridge | 相同输入可重复生成/验证，失败明确退出 |
| Runtime | 独立源快照与能力声明 | 正式管线消费结果并报告失败，相关回归通过 |
| Showcase | 同输入跨入口一致、缺源/冲突拒绝 | 第 6 节行为通过，并满足统一运行验收要求 |

主线已有相关测试入口（证明已有合同覆盖范围；不表示本页目标 UAT 已执行）：

- `src/Tests/ArchitectureTests/LogicTerrainFieldContractTests.cs`
- `src/Tests/ArchitectureTests/NavTerrainFeedDualTrackTests.cs`
