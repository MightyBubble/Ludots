# Map / ScenarioPlan / Ruleset / LaunchContext 边界合同

Parent: GitHub issue [#627](https://github.com/MightyBubble/Ludots/issues/627)。本页是 [#628](https://github.com/MightyBubble/Ludots/issues/628)（MSP-1）的正式架构合同。

本页只定名词、职责与红线。它不引入 `ScenarioPlan` schema、不改 MapConfig 合并实现、不改 runtime spawn。后续 MSP-2+ 必须引用本页，不得另起平行真相。

已有正式合同继续有效，本页只做边界归位，不重写其细则：

- [Map-Owned Participant Contract](map-owned-participant-contract.md)
- [Map Batch Performer Param Overrides](map-batch-performer-param-overrides.md)

## 1. 概述

玩家进入一局游戏时，系统其实在回答四个不同问题：

| 问题 | 正式归属 |
|------|----------|
| 这张世界长什么样？ | **Map** |
| 这一局开场怎么摆？ | **ScenarioPlan** |
| 可用的规则积木是什么？ | **Ruleset / Profile** |
| 这次是谁、以什么会话身份进入？ | **LaunchContext** |

当前 main 上，这四类数据仍有混放：

- 地图身份与开局摆放都挤在 `MapConfig` 里
- 强开局结构常藏进 `MapConfig.Metadata`
- Formation / MassNavigation 等 showcase 在 `MapLoaded` 后私有 `SpawnScenario`
- GoldMarket / FourX / TeamResearch 等逻辑局用 `World.Create` 直接造实体

目标架构是：同一张 Map 可挂多个 ScenarioPlan；Ruleset/Profile 继续提供可复用定义；LaunchContext 只描述进入会话。ScenarioPlan **不是** 第二份 MapConfig，也 **不是** 万能 deep-merge 层。

## 2. 结构

```text
Ruleset / Profile          Map                         LaunchContext
(可复用规则积木)            (世界身份)                   (进入会话)
        \                    |                              |
         \                   |                              |
          \------------> ScenarioPlan <---------------------/
                         (这一局开场摆放与局参数)
                                |
                                v
                     Map load materialization
                     (MapLoaded 之前完成正式开局)
```

四层各自只回答自己的问题：

| 层 | 一句话 | 典型内容 |
|----|--------|----------|
| Map | 世界本体 | 地形、棋盘、导航/碰撞、地图固定实体、固定触发器、默认相机、map-owned participant 绑定 |
| ScenarioPlan | 这一局怎么开场 | 放谁、放哪、朝向、归谁、实例补丁、performer 参数、初始资源/关系/库存、seed/布局旋钮 |
| Ruleset / Profile | 规则积木是什么 | entity template、performer、agent profile、ability/effect、relationship type |
| LaunchContext | 这次怎么进入 | `LocalPlayerId`、进入会话 metadata |

## 3. 详情

### 3.1 Map：世界身份

Map 描述“这张世界是什么”，不描述“这一局临时怎么开场”。

Map 拥有：

- 地形与视觉高度真相
- 棋盘（board）尺寸、空间域与 board 级空间配置
- 导航 / pathing / 结构碰撞等世界通行真相
- 地图固定实体（含 logical participant representative）
- map-owned team / player binding 与 participant relationship 的地图侧声明
- 地图固定触发器类型与默认相机

相关源码与合同：

- `src/Core/Config/MapConfig.cs`
- `src/Core/Map/MapManager.cs`
- `src/Core/Systems/MapLoader.cs`
- `src/Core/Engine/GameEngine.cs`（`LoadMap`）
- `src/Core/Engine/GameEngine.MapLoadLifecycle.cs`（`CompleteMapLoad`）
- [Map-Owned Participant Contract](map-owned-participant-contract.md)

Map 可以声明地图固有的固定单位与 participant；这些属于世界身份，不属于 ScenarioPlan。ScenarioPlan 负责的是“同一张世界上的不同开局摆放”。

### 3.2 ScenarioPlan：开局摆放白名单

ScenarioPlan 只选择并摆放既有规则积木，不得改写地图身份，也不得改写规则定义本体。

**允许表达的白名单：**

| 白名单项 | 含义 |
|----------|------|
| placement | 初始位置 |
| facing | 初始朝向 |
| team / player ownership | 队伍与玩家归属 |
| per-instance component patch | 实例级组件补丁 |
| performer params | 实例级 performer 参数覆盖 |
| initial resources / relationships / inventory | 开局资源、关系、库存 |
| seed / layout knobs | 随机种子与布局参数 |

**明确禁止：**

- 把 ScenarioPlan 做成通用 `MapConfig` merge / deep-merge 层
- 覆盖或改写地形、棋盘、导航、碰撞、结构碰撞资产
- 新增或修改 entity template / performer definition / agent profile / ability / effect / relationship type
- 改写默认相机、地图固定触发器类型表，或把它们伪装成“开局参数”
- 用无 schema 的 `Metadata` 数据袋承载上述强开局结构

ScenarioPlan 对 performer 参数与批量创建的语义，必须对齐既有 map batch 合同，而不是另造一套：

- map authoring 侧已有 `EntitySpawnData.PerformerParamOverrides`
- 正式细则见 [Map Batch Performer Param Overrides](map-batch-performer-param-overrides.md)
- 运行时队列侧已有 `RuntimeEntitySpawnRequest` 的位置、朝向、队伍/玩家归属、组件补丁字段（`src/Core/Gameplay/Spawning/RuntimeEntitySpawnQueue.cs`）

后续 MSP 实现必须让 ScenarioPlan materialization 走同一套白名单与失败语义：缺字段、非法字段、越权字段一律显式失败，禁止静默降级。

ScenarioPlan 可以声明“这一局”的队伍、玩家归属与初始关系意图，但最终 materialization 后仍必须进入 `MapSession` 的正式 participant / relationship 链路。它不得新建第二套 participant 容器、第二套 player/team lookup，或在 `MapLoaded` 后通过扫描补回代表实体。

### 3.3 Ruleset / Profile：规则定义

Ruleset / Profile 拥有可复用规则积木，不拥有某一局的摆放结果。

Ruleset / Profile 拥有：

- entity template
- performer definition
- agent profile
- ability / effect 定义
- relationship type 等规则类型

这些定义继续走 ConfigCatalog / ConfigPipeline 的正式合并策略。ScenarioPlan 只能引用它们的稳定 id，不能在开局计划里“顺便改模板正文”。

深度材料（非本页平行 SSOT）：

- `docs/architecture/config_pipeline.md`
- `docs/reference/config_data_merge_best_practices.md`

### 3.4 LaunchContext：进入会话

LaunchContext 描述“这次是谁、以什么会话身份进入这张已加载世界”，不描述世界身份，也不描述开局摆放。

正式字段与链路：

```text
MapLaunchContext.LocalPlayerId
  -> ParticipantBindingResolver
  -> CoreServiceKeys.LocalPlayerId / LocalPlayerEntity
  -> 输入与下单系统
```

相关源码与合同：

- `src/Core/Map/MapLaunchContext.cs`
- [Map-Owned Participant Contract](map-owned-participant-contract.md) 第 5 节

`MapLaunchContext.Metadata` 只允许承载进入会话所需的临时 payload。它不是 ScenarioPlan 的替代品，不得用来声明初始单位、棋盘、模板或强业务开局结构。

### 3.5 Metadata 退场原则

`MapConfig.Metadata` 与 `MapLaunchContext.Metadata` 都不得继续充当强 Scenario 数据的无 schema 数据袋。

| 容器 | 允许 | 禁止 |
|------|------|------|
| `MapConfig.Metadata` | 过渡期弱标注、非开局强结构的附属信息（后续 MSP 收口） | 初始单位表、队伍开局资源、关系初值、布局种子、performer 开局参数等强 Scenario 数据 |
| `MapLaunchContext.Metadata` | 进入会话临时 payload | 任何本应属于 Map / ScenarioPlan / Ruleset 的强结构 |

强 Scenario 数据必须进入有 schema、可校验的 ScenarioPlan；不得靠“先塞 Metadata，MapLoaded 后再解释”维持正式语义。

### 3.6 迁移目标，不是目标架构

以下路径是现状与迁移对象，不是目标架构：

| 现状路径 | 代表 | 目标收敛 |
|----------|------|----------|
| `MapLoaded` 后私有 `SpawnScenario` / EnsureScenario | Formation Capability、MassNavigation 相关 showcase | ScenarioPlan 在 `MapLoaded` 前完成 materialization |
| 直接 `World.Create` 开局 | GoldMarket、FourX、TeamResearch、部分 ParticipantView 逻辑局 | 走正式 spawn / ScenarioPlan / map-owned 路径 |
| `MapConfig.Metadata` 强业务结构 | ParticipantView、Blacksmith 等 metadata section | 有 schema 的 ScenarioPlan 或正式配置，而不是 Metadata 私货 |

`MapLoaded` 事件本身仍然存在，用于“地图已就绪后的玩法响应”；它不再承担正式开局造物职责。

## 4. 场景

### 4.1 同一张地图，多套开局

作者做好一张峡谷地图后，可以挂：

- 1v1 教学开局
- 压力测试开局
- showcase 演示开局

地形、棋盘、导航保持同一 Map；初始单位、归属、关系和局参数来自不同 ScenarioPlan。不必为每种开局复制整份 map JSON。

### 4.2 作者判断字段归属

作者需要同时声明：

- 初始单位位置 → ScenarioPlan
- 地图棋盘尺寸 → Map
- 技能模板 → Ruleset / Profile
- 本地玩家 → LaunchContext

### 4.3 大批量开局单位

大量初始单位与 per-instance performer 参数，走与 map batch / runtime spawn 对齐的白名单路径，而不是各 Mod 在 `MapLoaded` 后各写一套生成器。

### 4.4 非法 Scenario 越权

若 ScenarioPlan 试图声明地形、棋盘、导航覆盖或改写 template/performer 定义，加载必须显式失败，并说明这些字段不属于 ScenarioPlan。

## 5. 边界

### 5.1 本页负责

- 四层名词与职责合同
- ScenarioPlan 白名单与禁止项
- Metadata / MapLoaded 后置造物 / 直接 `World.Create` 的迁移原则
- 对既有 participant 与 map batch performer param 合同的引用关系

### 5.2 本页不负责（留给 MSP-2+）

- MapConfig 继承、InstanceId、Metadata 死字段收口（MSP-2）
- ScenarioPlan 最小 schema（MSP-3）
- runtime spawn 与 map batch 对齐实现（MSP-4）
- ScenarioPlan 预 `MapLoaded` materialization（MSP-5）
- Metadata 强业务结构迁移（MSP-6）
- Formation / MassNavigation tracer 迁移（MSP-7）
- Gold / FourX / TeamResearch 等逻辑局收敛（MSP-8）
- guardrails 与防回潮校验（MSP-9）

### 5.3 红线

1. **禁止** ScenarioPlan 成为通用 MapConfig merge 层。
2. **禁止** ScenarioPlan 修改地形 / 棋盘 / 导航 / 碰撞 / 模板定义 / performer 定义。
3. **禁止** 继续把强 Scenario 数据留在 Metadata 作为正式真相。
4. **禁止** 用 `MapLoaded` 私有补丁或直接 `World.Create` 充当目标开局架构。
5. **禁止** 另写一套与 [Map-Owned Participant Contract](map-owned-participant-contract.md)、[Map Batch Performer Param Overrides](map-batch-performer-param-overrides.md) 平行的 participant / performer-param 真相。
6. **禁止** fallback、静默失败、静默放过越权字段。

## 6. UAT

```gherkin
Feature: 架构合同可执行
  作为地图与开局作者
  我希望读完本合同就知道字段该放哪
  以免把开局摆放写进地图身份或规则定义

  Scenario: 新作者判断一个字段应该放在哪里
    Given 作者需要声明初始单位位置、地图棋盘尺寸、技能模板和本地玩家
    When 作者阅读本合同
    Then 初始单位位置归入 ScenarioPlan
    And 地图棋盘尺寸归入 Map
    And 技能模板归入 Ruleset/Profile
    And 本地玩家归入 LaunchContext

Feature: Map 与 ScenarioPlan 分层
  作为玩家
  我想在同一张世界上体验不同开局
  而不觉得每换一局就换了一张完全不同的地图

  Scenario: 同一张地图加载不同开局计划
    Given 玩家选择同一张地图
    And 选择两个不同的开局计划
    When 游戏分别加载这两局
    Then 地形、棋盘和导航保持一致
    And 初始单位、队伍、关系和局参数按各自开局计划生效
    And 加载过程没有依赖 Metadata 私货或 MapLoaded 后置补丁

Feature: 禁止万能 Scenario merge
  作为系统
  我必须拒绝把开局计划当成第二份地图配置

  Scenario: Scenario 尝试改地图身份
    Given 一个 ScenarioPlan 声明了地形、棋盘或导航覆盖
    When 游戏加载该 ScenarioPlan
    Then 加载必须显式失败
    And 错误信息说明这些字段属于 Map 而不是 ScenarioPlan

  Scenario: Scenario 尝试改规则定义
    Given 一个 ScenarioPlan 试图改写 entity template 或 performer 定义正文
    When 游戏加载该 ScenarioPlan
    Then 加载必须显式失败
    And 错误信息说明这些定义属于 Ruleset/Profile

Feature: 迁移目标不被当成正式架构
  作为维护者
  我需要分清现状捷径与目标路径

  Scenario: 作者看到旧 showcase 开局写法
    Given 仓库里仍存在 MapLoaded 后 SpawnScenario 或直接 World.Create 的 showcase
    When 作者阅读本合同
    Then 这些路径被标记为迁移目标
    And 目标架构是 ScenarioPlan 在 MapLoaded 前完成正式开局 materialization
```

## 相关入口

- Parent epic：[#627](https://github.com/MightyBubble/Ludots/issues/627)
- 本决策单：[#628](https://github.com/MightyBubble/Ludots/issues/628)
- Participant SSOT：[Map-Owned Participant Contract](map-owned-participant-contract.md)
- Performer param SSOT：[Map Batch Performer Param Overrides](map-batch-performer-param-overrides.md)
- 配置合并深度材料：`docs/architecture/config_pipeline.md`、`docs/reference/config_data_merge_best_practices.md`
