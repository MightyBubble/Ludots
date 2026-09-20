# WebUI Production / Worker / Queue Overview (WPK-4 / #603)

## 1. 概述

生产/工人/队列概览是 command/status/queue、GAS ability 状态与 `OrderBuffer` 的聚合展示视图，不是平行生产系统。本层在 WPK-1 Panel Kit manifest 与 WPK-3 CommandDeck 之上提供 profile 与 DataPlane topic 合同。玩家看到的是队列进度与工人分组，浏览器不维护独立生产队列。

## 2. 结构

```text
UI/production_overview_profiles.json
  -> ProductionOverviewProfileRegistry
      -> ProductionOverviewProjector
           (+ EntityCommandPanelSource status/queue
            + OrderBuffer
            + EntityCollectionStore worker members
            + tag/order/attribute match)
          -> ProductionOverviewWebUiTopicProducer (DataPlane)
              -> PanelKit manifest topic 订阅
```

| 字段 | 含义 |
|------|------|
| `id` | profile 稳定 id |
| `sourceKind` / `sourceRef` | 与 CommandDeck 同类的候选来源 |
| `commandPanelSourceId` | 既有 EntityCommandPanel source |
| `queueSourceKind` | `commandPanelSupplemental` 或 `orderBuffer` |
| `workerCollectionKey` | worker 实体集合 key |
| `workerBuckets[]` | idle / tag / orderType / attributePositive 分组 |
| `topic` | DataPlane topic（可与 PanelKit manifest 对齐） |

## 3. 详情

复用：WPK-1 `WebUiPanelKitManifest` / topic / profile / layout / `UiSurfaceHost`；WPK-3 同一 command source / collection / control-plane 语义；`EntityCommandPanel` 的 `CopyStatuses` / `CopyQueueItems` / revision；GAS `OrderBuffer` / `TagRegistry` / `AttributeRegistry`；`EntityCollectionStore` / `ControlPlaneView`。

投影行为：

- 生产队列只读 command panel status/queue，或成员 `OrderBuffer`；不复制生产真相。
- Worker 统计只读 entity collection + tag / orderType / attribute / idle 投影；前端不得猜状态。
- DataPlane payload 固定包含 `ownerEntityId`、`ownerVersion`、`profileId`、`revision`、`rows`、`queueItems`、`workerRows`、`blockedReasons`。
- Producer 成员解析：
  - `explicitEntity` / `solePossessedRep`：文档约定的单 owner 行为，解析到的 owner 即唯一 producer。
  - `entityCollection` / `controlPlaneView`：必须在 `sourceRef`（或 binding instanceKey）上拿到非空 producer collection；缺 store、缺 collection key/view、或空集合一律 fail-fast，错误含 profile id 与 sourceRef，禁止回退到 owner。
- 缺 command source、queue source、profile、worker match ref、producer collection 时 fail-fast，错误含具体 id。

禁止：新建 production store；UI 内 RTS flavor switch；缺 profile/source/producer collection 时 silent fallback。

## 4. 场景

- 星际：多个兵营训练中，面板显示每个队列和剩余进度。
- 帝国时代：空闲村民、采集村民、建造村民分组统计。
- C&C：建造队列、生产队列、升级进度在全局面板旁展示。

## 5. 边界

- 不定义新的生产规则；生产依旧由 GAS ability/order pipeline 决定。
- 面板只聚合展示和触发既有 command route。
- Build/Upgrade 作为 CommandDeck ability category/profile 视图，不另开后端。

## 6. UAT

```gherkin
Feature: 生产和工人概览
  Scenario: 玩家点击训练命令后队列显示进度
    Given 玩家拥有两个可训练建筑
    When 玩家从全局 CommandDeck 点击训练步兵
    Then Production 面板显示进入队列的训练项
    And 进度来自 command/status/queue 链路
    And 浏览器没有维护独立生产队列

  Scenario: 工人分组来自实体集合投影
    Given worker collection 含空闲、采集、建造成员
    And production overview profile 声明对应 bucket
    When WebUI DataPlane 发布 production revision
    Then 面板显示各 bucket 计数
    And 浏览器没有自行猜测工人状态

  Scenario: 缺 profile 或 command source 时失败
    Given production overview 引用未知 commandPanelSourceId
    When 安装 profile
    Then 安装失败且错误信息包含该 source id

  Scenario: entityCollection 缺 sourceRef 集合时不回退到 owner
    Given production overview profile 声明 entityCollection 与 sourceRef
    And 该 sourceRef 下没有 producer collection
    When 投影 production overview
    Then 投影失败且错误信息包含 profile id 与 sourceRef
    And 面板不会把 collection owner 当成唯一 producer 继续显示
```
