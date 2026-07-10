# WebUI Quest Objective Panel（WPK-6）

Objective / Quest tracker 面板的 DataPlane 投影合同。玩法真相只读 Quest core（`QuestRuntimeService` / Quest views / Quest events），不读 `NarrativeDirector`，也不经过 narrative 兼容层。

深度实现：`src/Libraries/Ludots.WebUI.DataPlane/QuestObjectiveWebUiTopicProducer.cs`。面板组合仍以 [WebUI Panel Kit Manifest (WPK-1)](webui_panel_kit_manifest.md) 为准；Quest SSOT 以 [Quest Core Infrastructure](quest_core_infra.md) 为准。

## 1. 概述

WPK-6 建立可复用的 Objective 面板数据投影：

- Panel Kit 用 `panelType=objective`、`profile.objective.generic`、manifest topic 声明组合。
- DataPlane producer 从 `QuestRuntimeService` 读取 active quest / stage / objective 文案与 revision。
- Quest stage/state 变化后，快照 `revision` 变化，浏览器可刷新目标列表。
- 多个 active quest 按 profile 过滤与排序。
- 缺 Quest definition、stage、objective text/token、profile 时 fail-fast，错误含具体 id。
- 不引用 `NarrativeDirector`；Narrative 可发事件驱动 Quest，但面板状态只来自 Quest core。

## 2. 结构

```text
QuestRuntimeService (SSOT)
    -> QuestObjectiveWebUiTopicProducer (DataPlane topic)
        -> QuestObjectiveWebSnapshot (profile / owner / revision / quests[])
            -> Panel Kit manifest panelType=objective 订阅该 topic
```

| 构件 | 职责 |
|------|------|
| `QuestObjectivePanelProfile` | 包含哪些 `QuestState`、排序键、可选 quest id / tag 过滤 |
| `IQuestObjectiveTextValidator` | 校验 objective 明文或 PresentationText token（WPK-5 钩子） |
| `QuestObjectiveWebUiTopicProducer` | 读 Quest views，投影 JSON snapshot |
| `WebUiQuestObjectivePanelDescriptors` | Panel Kit 侧稳定 panelType / profile / sample topic id |

## 3. 详情

### 3.1 复用

- `QuestRuntimeService.GetQuestViews` / `TryGetDefinition` / `TryGetStage` / `QuestEventPublished` / `QuestInstanceCm.Revision`
- `IWebUiTopicProducer` / `WebUiOutboundPacket` / LatestWins 订阅模型
- WPK-1：`profile.objective.generic`、`panel-kit.sample.objective`、`layout.list.vertical`
- 可选：`PresentationTextCatalog.GetTokenId` 作为 token 存在性钩子（WPK-5）；未接线时禁止声明 token，禁止静默忽略

### 3.2 新增

- DataPlane：`QuestObjectivePanelProfile`、`QuestObjectiveTextValidator`、`QuestObjectiveWebUiTopicProducer`
- PanelKit：`WebUiQuestObjectivePanelDescriptors`
- Quest stage 可选字段：`ObjectiveTextToken` / `ObjectiveHintToken`（与现有 `ObjectiveText` / `ObjectiveHint` 并存）
- `QuestView` 增加 `ScopeHost`、`Revision`，供面板投影 owner/scope 与 revision

### 3.3 Snapshot 形状

- `profileId`、`ownerEntityId/WorldId/Version`、`revision`
- `quests[]`：`questId`、`displayName`、`summary`、`state`、`stageId`、`stageTitle`、`objectiveText`、`objectiveHint`、token 引用、`questRevision`、quest/scope entity 坐标

### 3.4 Fail-fast

- 未知 / 未注册 Quest definition（含 profile allow-list）
- Active 投影行缺少 stage
- 缺 `ObjectiveText` 且缺 `ObjectiveTextToken`
- 声明了 token 但未配置 WPK-5 钩子，或 token 未注册
- 空 / 未知 profile 构造参数

禁止空串兜底、Unknown、静默跳过坏数据。

### 3.5 文案合同与 WPK-5

当前 Quest stage 以 `ObjectiveText` / `ObjectiveHint` 为可用明文合同。若配置 `ObjectiveTextToken`，必须提供 PresentationText 解析钩子；完整 localization 目录与 HUD 绑定属于 WPK-5，本层只做显式校验钩子，不做 fallback 明文替换。

## 4. 场景

- 大战略：当前角色目标、决议链、事件任务 — 同一 producer，换 profile 过滤 tag / allow-list。
- 4X：研究 / 扩张 / 危机目标 — 多 quest 按 profile 排序显示。
- RTS：教学任务、战役目标、支线 — scope host 过滤到当前玩家上下文。

## 5. 边界

- 不做 Dialogue / Cinematic 面板。
- Notification 可展示 quest 事件，但不拥有 quest state。
- 不把具体游戏任务名写进可复用 PanelKit / DataPlane 代码。
- 不读 `NarrativeDirector`，不建 narrative 兼容投影层。
- 不做完整 localization 运行时（WPK-5）；只校验 token 存在性钩子。

## 6. UAT

```gherkin
Feature: Quest Objective 面板
  Scenario: 任务阶段变化后目标刷新
    Given 玩家有一个进行中的 Quest
    When QuestRuntimeService 发布阶段切换
    Then Objective 面板显示新的目标文本和阶段状态
    And 面板快照 revision 已变化
    And 面板没有读取 NarrativeDirector

  Scenario: 多个进行中任务按 profile 排序
    Given 玩家同时有多个进行中的 Quest
    And Objective profile 指定了排序或过滤规则
    When 打开 Objective 面板
    Then 面板只显示 profile 允许的任务
    And 任务按 profile 规定的顺序排列

  Scenario: 缺任务定义或目标文案时加载失败
    Given Quest 定义、阶段或目标文案/token 缺失
    When Objective 面板请求快照
    Then 投影失败
    And 错误信息包含缺失的具体 quest、stage 或 token id
```

## 源码与测试

- Producer：`src/Libraries/Ludots.WebUI.DataPlane/QuestObjectiveWebUiTopicProducer.cs`
- Profile / text：`QuestObjectivePanelProfile.cs`、`QuestObjectiveTextValidator.cs`
- PanelKit 描述符：`src/Libraries/Ludots.WebUI.PanelKit/WebUiQuestObjectivePanelDescriptors.cs`
- 测试：`src/Tests/WebUiDataPlaneTests/QuestObjectiveWebUiTopicProducerTests.cs`
- PanelKit 回归：`src/Tests/WebUiPanelKitTests/WebUiPanelKitManifestTests.cs`
