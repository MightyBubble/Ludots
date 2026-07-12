# 实体仿真阶段验收

本文定义“大规模实体仿真”工作的阶段性 UAT。验收目标不是只看单点功能，而是确保每一阶段都能给团队提供明确、可观察、可复现的结果。

## 1 验收原则

- 每个阶段都必须有独立入口
- 每个阶段都必须有用户可观察结果
- 每个阶段都要记录性能与语义
- 没有通过本阶段 UAT，不进入下一阶段主集成

## 2 UAT-0：contract 与单写真相

### 2.1 验收目标

确认正式组件 spec、车道分工、AOI 服务口径和位置单写规则已稳定。

### 2.2 验收项

- 任何一个 entity 都能回答：
  - 是否 authority
  - 是否 budgeted
  - 属于哪条 avoidance lane
  - 由哪条热路径写位置
- `WorldPositionCm`、`Position2D`、crowd SoA、`VisualTransform` 的职责边界有正式文档
- AOI 主语义是 `ILoadedChunks`，不是 `HexGridAOI` 专名

### 2.3 通过标准

- 文档、代码、任务拆分三者口径一致
- 无“同一个 entity 双写位置”的未决灰区

## 3 UAT-1：精确物理车道决议（#643）

### 3.1 验收目标

当前没有可执行的精确物理车道 UAT。历史 `FormationPhysics` 枚举与解析入口已经删除，也不存在正式的 `FormationPhysicsPlaygroundMod`。[#643](https://github.com/MightyBubble/Ludots/issues/643) 继续负责中性精确物理车道的真实需求与验收入口。

### 3.2 启动条件

- 若 #643 决定删除，该 UAT 一并删除。
- 若 #643 决定中性化，必须先实现真实消费者和正式可玩入口，再补玩家视角的 Cucumber 场景。
- 在此之前不得用 Formation Capability Showcase 冒充精确物理车道验收；该 Showcase 使用 MassNavigation 明确成员目标链路。

### 3.3 当前通过标准

- 文档、矩阵和任务不再宣称不存在的 lane、playground 或性能结果已经交付。
- #643 有明确的删除或中性化决议。

### 3.4 后续指标

只有 #643 选择中性化并完成实现后，才记录 100、300、1000 三档的 `fps`、`frame ms`、物理步数、接触对数量和每帧物理解算时间。

## 4 UAT-2：MassNavigation 车道

### 4.1 验收目标

确认大量 crowd 已走正式 SoA / CrowdFlow 车道，并能执行明确空间目标和局部避让。Formation 业务只作为上层目标生产者，不进入 Core 验收口径。

### 4.2 玩家验收场景

```gherkin
Feature: 大规模单位移动

  Scenario Outline: 玩家向大规模单位下达移动命令
    Given 玩家进入正式的大规模导航战场
    And 战场中有 <规模> 个可移动单位
    When 玩家框选一批单位并右键点击远处地面
    Then 被命令的单位向目标区域移动
    And 单位在密集区域会绕开彼此与地图障碍
    And 战场保持可操作，不出现单位静默停摆

    Examples:
      | 规模 |
      | 2000 |
      | 5000 |
      | 10000 |

  Scenario: Formation Showcase 通过明确成员目标驱动士兵
    Given 玩家进入 Formation Capability Showcase
    And 玩家选中了一个方阵控制单位
    When 玩家右键移动并使用旋转操作改变朝向
    Then 控制单位移动到目标区域
    And 士兵绕开障碍后重新聚集到各自槽位
    And 旋转操作不会制造一次“移动到当前位置”的伪移动命令
```

### 4.3 期望结果

- crowd 不依赖 `FullPhysics2D` 也能稳定运行
- Formation Showcase 的成员目标来自 Showcase 自己的业务状态，并通过 MovePlanning execution sink 进入 MassNavigation
- 成员之间存在可感知的分离或避让
- 可见规模提升时仍保持可玩
- 通用 `massNavigationMove` 不包含 formation mode、slot 或 rotation payload

### 4.4 必记指标

- 2k、5k、10k 三档的：
  - `fps`
  - `frame ms`
  - crowd step 耗时
  - prepare / steer / resolve / sync 耗时
  - 可见 crowd 数量

## 5 UAT-3：AOI / LOD / 预算

### 5.1 验收目标

确认 budgeted entity 会随 AOI 与 LOD 进入降频、休眠或去物质化，而 authority entity 不会被误裁。

### 5.2 玩家验收场景

```gherkin
Feature: 大地图仿真预算

  Scenario: 玩家在不同战区之间移动镜头
    Given 玩家进入包含控制单位、路人和羊群的大地图
    When 玩家把镜头从当前战区移到远处战区并来回切换
    Then 关键控制单位始终保留其战斗和命令状态
    And 远处非关键单位可以降低更新频率或暂时休眠
    And 玩家返回原战区时仍能看到一致的业务结果
    And 预算面板能展示数量和资源消耗变化
```

### 5.3 期望结果

- authority entity 不会因镜头离开而失去后端真相
- budgeted entity 会随距离或 interest 变化进入降频
- AOI 服务不要求 hex board 才能工作
- 预算收益能在面板或日志中直接看到

### 5.4 必记指标

- 近景、中景、远景三档的：
  - materialized 数量
  - dormant 数量
  - dematerialized 数量
  - simulation budget 消耗
  - 可见实体数量

## 6 UAT-4：统一 showcase

### 6.1 验收目标

确认团队有一个统一入口能同时观察 authority entity 后端真相、crowd 表演和 AOI / LOD 预算；Formation 只在其业务 Showcase 中作为明确目标生产者出现。

### 6.2 玩家验收场景

```gherkin
Feature: 统一实体仿真展示

  Scenario: 玩家在同一入口观察移动、避让和预算
    Given 玩家进入统一实体仿真 showcase
    And 场景中存在关键控制单位和大量普通单位
    When 玩家下达多组移动命令并切换观察区域
    Then 单位会移动、避让并在目标区域稳定下来
    And 远处普通单位可以降频或休眠
    And 关键控制单位继续保持业务真相
    And 面板展示车道、预算和关键性能指标
```

### 6.3 期望结果

- 已实现的后端 authority 仿真和 crowd 表演同时成立；不得把 #643 未决车道写入通过结论
- 远距离只裁剪或降频 budgeted entity
- performer 只负责表现，不承载逻辑真相
- 面板能展示：
  - lane 分布
  - LOD 分布
  - budget 消耗
  - 关键性能指标

### 6.4 必记指标

- 统一 showcase 下的：
  - authority 数量
  - budgeted 数量
  - visible / dormant / dematerialized 数量
  - 已实现的 physics 车道耗时；#643 若删除候选车道则不保留空指标
  - crowd 车道耗时
  - 总 `fps`
  - 总 `frame ms`

## 7 不通过条件

出现以下任一情况，本阶段 UAT 视为不通过：

- authority entity 会因镜头离开而失去逻辑真相
- 同一个 entity 同时被两条热路径写位置
- 为了“统一”而把万级 crowd 强行塞进 `FullPhysics2D`
- 只剩碎功能，没有独立可演示入口
- AOI 接口只能在 hex board 下工作

## 8 UAT 输出要求

每个阶段都必须至少输出：

- 验收入口名
- 测试步骤
- 通过 / 不通过结论
- 性能记录
- 已知限制
- 下一阶段依赖是否已满足
