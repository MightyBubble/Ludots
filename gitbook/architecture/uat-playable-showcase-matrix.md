# UAT 可玩 Showcase 矩阵

本文把当前仓库内已经具备正式入口、可玩运行态、可复用验收链路的 showcase / playground 收敛成一套 UAT 矩阵。

目标不是再造一套新 demo 体系，而是复用已有 mod、地图、runtime、panel 与 acceptance 路径，给每个 UAT 阶段一个明确的主入口。

## 1 选型原则

- 必须是正式 `mod.json + assets/game.json + startupMapId` 入口
- 必须已有可观察玩家操作，不是纯 headless fixture
- 必须能复用现有 acceptance / screenshot / trace 工件链路
- 必须服从单写真相，不新增 showcase 私有 contract
- 必须能说明自己对应哪个 UAT 阶段，而不是“什么都能演示一点”

## 2 推荐矩阵

| UAT | 主 showcase | 入口 map | 目标 | 复用重点 |
|------|-------------|----------|------|----------|
| `UAT-1` | 未分配（由 #643 决议） | 无 | 历史精确物理车道的删除或中性化 | 当前没有正式 playable entry，不得虚构 playground |
| `UAT-2` | `CapabilityStandardMassNavigationLargeWorld10kMod` | `mass_navigation` | 大规模 crowd、MassNavigationFlow 执行、drag-select、move command | 复用 `MassNavigationMod` runtime、panel、contract tests 与 performer tests |
| `UAT-2` 业务消费者 | `FormationCapabilityShowcaseMod` | `formation_capability_showcase` | Showcase-owned formation move/rotate、明确逐成员目标、障碍后重聚 | 复用 OrderQueue、MovePlanning execution sink 与 MassNavigation agent binding |
| `UAT-3` | `RelationshipShowcaseMod` | `relationship_showcase` | 预算、状态、前端场景卡、artifact 产出链路 | 复用 production battle-report / trace / path artifact 输出模式 |
| `UAT-4` | `InteractionShowcaseMod` | `interaction_showcase_hub` | 统一入口、控制组、formation 视图、entity info、HUD 面板、跨系统联动 | 复用 hub/stress 双地图、selection dock、entity collection inspector、playable acceptance |

## 3 UAT-1：#643 决议前不分配入口

### 3.1 为什么是它

- 仓库当前没有 `FormationPhysicsPlaygroundMod` 或 `formation_physics_playground`。
- 历史 `FormationPhysics` 枚举与解析入口已经删除；[#643](https://github.com/MightyBubble/Ludots/issues/643) 只继续治理中性精确物理车道。
- Formation Capability Showcase 使用 MassNavigation 明确成员目标，不是精确 Physics2D lane 的替代证据。

### 3.2 后续操作

- #643 若决定删除：从矩阵移除 UAT-1。
- #643 若决定中性化：完成真实实现与正式 launcher 后，再按 Cucumber 玩家场景补入矩阵。

### 3.3 通过标准

- 当前通过标准只是：不再宣称不存在的 lane、Mod、地图或测试已经交付。

## 4 UAT-2：CapabilityStandardMassNavigationLargeWorld10kMod

### 4.1 为什么是它

- 它是当前 MassNavigationFlow 大规模执行基线
- 它通过地图 `mass_navigation` 与 `MassNavigationMod` 使用正式执行引擎
- 它覆盖 team-slot、direct target、邻居分离、硬解析、performer 展示与配置面板
- NAV-6 到 NAV-9 的路由、per-agent target、move-plan 与 road 迁移都以 MassNavigationFlow 为执行 sink
- 它不加载 formation identity、slot layout、facing、rotation 或 follower runtime

### 4.2 玩家验收场景

```gherkin
Feature: 万级大规模导航

  Scenario: 玩家向大规模单位下达移动命令
    Given 玩家进入 mass_navigation 战场
    When 玩家拖框选中一批单位并右键点击远处地面
    Then 被选中的单位向目标区域移动
    And 单位在密集区域会局部避让
    And HUD 会持续显示到达数量和运行状态
    And 大规模单位与 Road Network 的路线移动可以分别通过正式执行链运行
```

### 4.3 通过标准

- crowd 在大规模下仍可选、可命令、可重现
- MassNavigationFlow panel / runtime 不因规模增加而丢失交互
- route-to-execution 和 move-plan sink 都使用正式 runtime，不使用脚本桩
- contract tests 能覆盖配置、执行、arrival、performer 与 road move-plan 回归
- `massNavigationMove` 只携带一个明确空间目标

### 4.4 Formation Capability 业务消费者

`FormationCapabilityShowcaseMod` 自己拥有 `formationMove`、`formationRotate`、成员关系、槽位和朝向。它把逐成员目标转换为 `MovePlanExecutionIntent`，再通过 `MassNavigationMovePlanExecutionSink` 交给通用导航执行。

```gherkin
Feature: 方阵业务通过明确成员目标使用大规模导航

  Scenario: 玩家移动并旋转方阵
    Given 玩家进入 Formation Capability Showcase
    And 玩家选中了一个方阵
    When 玩家右键移动并使用旋转操作
    Then 方阵和士兵向新的目标姿态移动
    And 士兵绕开障碍后回到各自槽位
    And 旋转不会提交伪移动订单
```

## 5 UAT-3：RelationshipShowcaseMod

### 5.1 为什么是它

- 它现成具备前端驱动、场景卡、artifact 输出链路
- 非常适合作为“预算 / 语义 / 展示证据”的标准模板
- 可用于把 AOI / LOD / 预算状态展示为用户可观察的结论，而不是只看控制台

### 5.2 玩家验收场景

```gherkin
Feature: 可回放的预算与状态证据

  Scenario: 玩家运行关系场景并查看结果
    Given 玩家进入 relationship_showcase
    When 玩家运行一个场景并等待结果生成
    Then 玩家能看到状态变化和场景卡结果
    And 系统产出 battle report、trace 与 path 证据
    And 后续 AOI、LOD 与 budget 结果可以沿用同一证据形式
```

### 5.3 通过标准

- scenario card、battle report、trace、path artifact 全部产出
- 状态变化能由用户侧证据回放，而不是只依赖日志
- 该链路可被后续 AOI / LOD 阶段复用

## 6 UAT-4：InteractionShowcaseMod

### 6.1 为什么是它

- 它已经是统一入口风格的 showcase
- 有 `hub` 与 `stress` 双地图
- 有 selection dock、control group、formation view、entity collection inspector、ability HUD
- 是目前最接近“统一 showcase”定义的现货

### 6.2 玩家验收场景

```gherkin
Feature: 统一交互入口

  Scenario: 玩家管理多组单位并查看信息
    Given 玩家进入 interaction_showcase_hub
    When 玩家多选单位并保存到 control group
    And 玩家切换 formation view 与 live view
    And 玩家打开 entity info 与 collection inspector
    Then control group 可以召回
    And HUD 与实体信息保持一致
    And 切换视图不会产生第二套选择真相

  Scenario: 玩家进入高压交互路线
    Given 玩家从 hub 进入 stress 路线
    When 玩家连续执行输入和技能组合
    Then 输入、技能和 HUD 仍保持一致反馈
```

### 6.3 通过标准

- 一个入口里同时看到 selection、orders、HUD、entity info
- entity collection panel 只消费正式 semantic preview contract
- formation / live selection 视图切换不产生第二套 truth
- 统一入口能继续挂接后续 budget / crowd / authority 观测面板

## 7 本轮推荐落地顺序

1. `CapabilityStandardMassNavigationLargeWorld10kMod`
2. `FormationCapabilityShowcaseMod`
3. `RoadNetworkShowcaseMod`
4. `InteractionShowcaseMod`
5. `RelationshipShowcaseMod`

这个顺序的目的：

- 先跑通 `UAT-2` 的通用 MassNavigation 基线和 Formation 业务消费者
- `UAT-1` 等待 #643，不用虚构入口填空
- 再用 road showcase 回归 route / move-plan / MassNavigationFlow sink
- 再用 `InteractionShowcaseMod` 做 `UAT-4` 统一入口
- 最后用 `RelationshipShowcaseMod` 补强 artifact-first 的证据产出模板

## 8 禁止事项

- 不新增一个“UAT 专用 launcher”绕开现有 mod 入口
- 不在 showcase 里复制 Core / capability contract
- 不靠硬编码 English copy 让面板看起来“能用”
- 不把 acceptance 变成依赖未注册 attribute 名称的隐式行为测试

## 9 后续实现建议

- #643 决定是否删除 UAT-1，或在真正实现中性精确物理车道后另补可玩入口
- `CapabilityStandardMassNavigationLargeWorld10kMod` 继续补 MassNavigationFlow 档位记录与统一性能字段
- `FormationCapabilityShowcaseMod` 继续验证 formation 业务只通过 OrderQueue 与 MovePlanning execution sink 接入
- `RoadNetworkShowcaseMod` 继续承接 route / move-plan / MassNavigationFlow sink UAT
- `InteractionShowcaseMod` 作为 UAT-4 主入口，继续收敛 selection / entity-info / HUD 的正式 contract
- `RelationshipShowcaseMod` 作为 artifact-first 模板，承接 AOI / LOD / budget 证据产出
