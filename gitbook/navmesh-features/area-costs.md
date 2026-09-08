# 区域分类与 Agent 通行代价

> [功能目录](README.md) · [体系总合同](../navmesh-ssot.md)
>
> 本页落实 [#372](https://github.com/MightyBubble/Ludots/issues/372) 的既定合同。第 1–6 节为产品设计，第 7 节区分主线实现、未合分支与剩余接线。

## 1. 概述

同一块泥地，对侦察兵和运输兵可以有不同通行代价。地图只声明“这里是泥地”，查询再按当前 Agent 的策略决定怎么走：

```text
通行代价 = f(区域分类, Agent 通行策略)
```

`areaId/tags` 是跨玩法共享的区域分类，属于地图/地形领域。导航、区域效果、视野等消费方各自解释分类；导航代价不能写回地形格、语义 sidecar 或 NavTile。

这不是待新建的矩阵系统。主线的 `Navigation/pathing.json` 已通过 `agentTypes[].navMesh.areaCosts[]` 表达每个 Agent 的一行代价数据。

## 2. 结构

| 数据 | 唯一归属 | 消费方式 |
|---|---|---|
| areaId / tags | 地图/地形分类 | 导航烘焙传播分类；其他玩法直接消费同一分类 |
| 半径、身高、层 | AgentProfile | 决定几何可行性，按 profileId 引用 |
| 每个 Agent 的区域代价 | PathingConfig.agentTypes[].navMesh.areaCosts | 当前 Agent 编译成自己的 NavAreaCostTable |
| 路径和总成本 | 查询结果 | 使用本次请求的 AgentTypeId、分类和代价表 |
| 区域效果、视野规则 | 各玩法消费方 | 以同一 areaId/tags 查自己的规则，不依赖导航 cost |

两个 Agent 可以引用同一个几何 profile，同时使用不同代价。为了改变道路偏好，不应复制 AgentProfile 或重新烘焙一套几何。

## 3. 详情

### 3.1 编辑器功能

- **区域分类面板**只编辑分类名称、areaId/tags、空间范围和显示颜色。
- **Agent 通行矩阵**按“行 = Agent 类型，列 = 区域分类”编辑代价，格子明确标出当前行的 Agent。不能给泥地设置一个全局默认 cost。
- 路径模拟先选 Agent 类型，界面显示它引用的几何 profile 和代价行；切换 Agent 后重新查询。
- 只改某 Agent 的代价时，标记该策略已修改，不标记地形重烘焙。改变区域分类或范围时才更新分类产物。
- 不可通行使用明确过滤规则，不能用极大 cost 或非法数值替代。

### 3.2 工具功能

复用当前 `PathingConfig` 与严格 loader。以下片段摘取现有字段说明矩阵关系；完整文件还需 selection、nodeGraph 等必填字段，见 [作者工具链现有配置示例](../reference/navmesh-authoring-bake-toolchain.md)。

```json
{
  "agentTypes": [
    {
      "id": "scout",
      "profileId": "infantry",
      "navMesh": {
        "areaCosts": [
          { "areaId": 2, "cost": 1.0 },
          { "areaId": 3, "cost": 1.0 }
        ]
      }
    },
    {
      "id": "carrier",
      "profileId": "infantry",
      "navMesh": {
        "areaCosts": [
          { "areaId": 2, "cost": 1.0 },
          { "areaId": 3, "cost": 4.0 }
        ]
      }
    }
  ]
}
```

这里 2 和 3 分别引用地图已有的道路、泥地区域分类，两个 Agent 共用 `infantry` 几何产物。数值仅为演示配置，不是引擎默认值。

工具分开校验几何输入与 Agent 策略。只改矩阵的一行，应保持区域分类、NavTile 字节和几何 build hash 不变；策略版本变化只影响相应 Agent 的路径结果。

### 3.3 Runtime 功能

正式请求携带 `AgentTypeId`。已有 `AutoPathService.CompileAgent` 根据每个 `PathingAgentTypeConfig` 编译 `NavAreaCostTable`；解算时将当前 Agent 的表传给查询。

```text
PathRequest.AgentTypeId
  → PathingConfig 中对应 Agent 的策略
  → profileId 选择几何产物
  → 该 Agent 的 NavAreaCostTable 解释 polygon areaId
  → 路径与成本
```

- 每个生产入口都必须保留请求的 Agent 身份，不能统一绑定 `AgentTypes[0]`。
- 两个 Agent 可共享 Detour 几何；每次查询的过滤器/代价表必须隔离。
- 路径结果缓存包含 Agent 策略身份和版本；几何缓存不因单改 cost 而重建。
- 静态配置重载和运行中策略修改要有明确应用时机。即时矩阵编辑是目标工具能力，不能把当前加载时编译宣称为已支持热改。

## 4. 场景与 Showcase 设计

### 一句话与目标用户

同一块泥地，两种单位各有自己的路线。

给作者和玩家看清“道路偏好由单位决定”。主演示使用相同体型、相同可行几何的侦察兵和运输兵，排除半径差异造成的绕行。

### 主循环

0–15 秒：同时派两队去同一营地，侦察兵走短泥路，运输兵走长公路。15–35 秒：只降低运输兵那一行的泥地代价，应用策略并重新下令。35–60 秒：运输兵改走泥路，侦察兵路径保持不变；恢复原策略重试。

惊喜时刻是只改一个矩阵单元就改变一队的路线，地图和另一队完全不动。界面在 1 秒内确认操作；若策略尚未应用，显示等待而不伪造新路径。

### 消融对照

切换“两个 Agent 使用相同矩阵行 / 使用各自矩阵行”。相同行时两队选择相同路径，各自行时路线分开。两侧都使用正式查询，保留通行过滤和相同几何。

### 解释层

侦察兵绿线、运输兵橙线；HUD 显示 Agent 类型、几何 profile、经过的区域、该 Agent 在各区域的代价、总成本和策略版本。地形分类图保持相同，NavTile build hash 保持不变。

### 旋钮清单

取值来自演示配置，以下操作在同一会话经正式策略接口生效。

| 运行中操作 | 范围 | 回答的问题 |
|---|---|---|
| 选中 Agent | 侦察兵 / 运输兵 | 正在编辑谁的道路偏好 |
| 该 Agent 的泥地代价 | 配置允许的正值区间 | 只影响当前单位类型吗 |
| 该 Agent 的道路代价 | 配置允许的正值区间 | 长路是否值得走 |
| 目标营地 | 地图声明的终点 | 改目标后策略是否仍有效 |
| 消融策略 | 相同矩阵行 / 独立矩阵行 | 路线差异是否来自 Agent 策略 |
| 应用并重新下令 | 当前队伍 | 实际移动是否采用新策略 |

### 场景结构

主演示：同体型双 Agent × 短泥路/长公路。子场景：纯 NavMesh 地图入口、只有高代价通路、无效矩阵引用、策略更新后缓存隔离。

首屏：“同时派两队去营地，再只改运输兵的泥地代价。”

跨域分类复用由 #372 保留独立验收：同一区域可被区域效果规则消费，导航矩阵修改不应改变该效果。该场景不把 GAS 逻辑塞进 NavMesh。

### 门户资产

封面取两队路线分开的真实画面；矩阵、地图、图例和媒体由正式配置及 `showcase.registry.json` 关联。不能手写第二份示例路线或成本数字当作运行结果。

### 反向 API 审计

| 所需能力 | 归属 | 已有与剩余 |
|---|---|---|
| per-Agent cost 编译和查询 | Pathing / Query | 主线 AutoPathService 已有 |
| 纯 NavMesh 入口保留 AgentTypeId | GameEngine / Pathing | 主线仍绑定首 Agent；本地 #1402 分支有候选修复 |
| 分类去 Cost 与中立归属 | Map/Terrain | #372；旧 #1006 分支有去 Cost/分类投影片段 |
| 运行中矩阵编辑与策略版本 | Pathing / Editor | 尚需明确应用时机和路径失效 |
| 逐段 Agent 成本诊断 | Query / Presentation | 待作者可读接口 |

### 交付边界与完成判据

Showcase 状态：设计完成，所述完整演示尚未实现，不可玩。复用现有矩阵和查询服务完成接线；按 [统一交付合同](showcase-delivery.md) 验证双 Agent 的真实移动、消融和故障反馈。

## 5. 边界

- 只有分类 key 进入地图/sidecar/NavTile，不存全局或 per-Agent cost。
- AgentProfile 是几何能力，AgentType 是通行策略，两者通过 profileId 关联；两份职责明确的配置本身不是重复来源。
- 高代价不等于禁止通过。物理不可行、规则禁行与偏好代价分别处理。
- #372 是既定规则来源；#1345 只负责导航分类接线，#479 只负责编辑器操作，不另造词汇表或矩阵。
- 历史分支只按缺口提取，不恢复旧 .ltrn/CDT/独立编辑器管线。

## 6. UAT

```gherkin
Feature: 区域分类与 Agent 通行代价正交

  Scenario: 同体型单位按各自策略选择路线
    Given 侦察兵和运输兵具有相同体型并能通过两条道路
    And 运输兵对泥地的代价高于侦察兵
    When 玩家让两队从同一起点前往同一营地
    Then 侦察兵走短泥路
    And 运输兵走长公路
    And 两队都实际抵达
    And 面板显示两者使用相同几何产物

  Scenario: 只修改一个 Agent 的矩阵行
    Given 两队已经按各自策略查询过路径
    When 作者只降低运输兵的泥地代价并应用
    And 玩家向两队重新下达相同目标
    Then 运输兵选择新的低代价路线
    And 侦察兵的路线和代价不变
    And 地图分类与 NavTile build hash 不变
    And 工具不执行几何重烘焙

  Scenario: 纯 NavMesh 地图不丢失单位身份
    Given 地图只加载 NavMesh 且声明两种 Agent 策略
    When 玩家交替指挥两种单位前往同一目标
    Then 每次查询都显示当前单位自己的策略
    And 第二种单位不会沿用第一种单位的代价表

  Scenario: 高代价唯一通路仍可通行
    Given 当前 Agent 到营地只有一条合法泥地道路
    When 玩家下达移动命令
    Then 单位沿泥地道路抵达
    And 面板显示较高成本而非禁行
```

这些是目标验收，不是本次已通过的运行记录。

## 7. 现状与 TODO

基线：2026-09-08，`origin/main 9231f05fcf`。

| 位置 | 核实结果 | 接收动作 |
|---|---|---|
| #372 | open，明确 cost = f(area, agent)，分类层无 cost | 继续作为规则来源，不能新建平行矩阵需求 |
| #412 | closed，已并入 #372/#1342/#1345 | 关闭表示范围整合，不代表全部代码已进 main |
| 主线 PathingConfig / AutoPathService | 已有每 Agent 的 areaCosts 与编译/查询消费 | 复用，不能列为从零开发 |
| 主线 LogicTerrainCell / NavMeshBakeConfig.Areas | 仍含 Cost 字段 | 现有残留，不是目标 schema；按 #372 清理 |
| 主线 GameEngine 纯 NavMesh 入口 | CreateDefaultNavMeshPathService 取 AgentTypes[0]，注册单 query adapter | 补逐请求 AgentTypeId 选择，不能宣称所有入口已正交 |
| 本地 codex/issue-1402-route-init，5bd22c9e4e；codex/issue-1402，1d2eb4a2ec | 已有纯 NavMesh Auto 请求按 AgentTypeId 选择的代码与回归，未在 main | 按当前 API 提取；现有分支测试不等于双 Agent 成本实机验收 |
| 本地/远端 codex/terr-399-merge-main，f81f2b240d / PR #1006 | 分支 Map/Fields/LogicTerrainField.cs 已去 Cost，80ddd77b7a 有分类投影；PR closed 且未合并 | 只提取分类/去 Cost 合同，旧持久化和 CDT 整包不回迁 |
| codex/nav-bake-policy | 仍保留 LogicTerrainCell.Cost | 不能把它当作 cost 正交已完成的分支 |

| 责任层 | 本页剩余功能点 | 验收 |
|---|---|---|
| 编辑器 | #479 展示 Agent × area 矩阵，明确当前 Agent 行 | 修改一行只影响一种策略 |
| 工具 | #372/#1345 分类不写 cost；输出策略/几何各自指纹 | 单改矩阵不重烘焙 |
| Runtime | 补纯 NavMesh 请求身份接线、隔离策略和路径缓存 | 同几何不同 Agent 走不同路 |
| Showcase | 双 Agent 主循环与相同行消融 | 正式输入、真实到达、几何 hash 不变 |

主线证据：

- `src/Core/Navigation/Pathing/Config/PathingConfig.cs`
- `src/Core/Navigation/Pathing/AutoPathService.cs`
- `src/Core/Engine/GameEngine.cs`
- `src/Core/Navigation/NavMesh/NavAreaCostTable.cs`
- `src/Core/Navigation/Terrain/LogicTerrainField.cs`
- `src/Core/Navigation/NavMesh/Config/NavMeshBakeConfig.cs`
- `src/Tests/ArchitectureTests/LogicTerrainFieldContractTests.cs`（已有分类传播测试，不代表矩阵全链已验收）
