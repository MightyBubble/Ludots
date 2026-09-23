# WebUI TechTree / Progression Panel（WPK-9）

TechTree 面板以后端 Progression / requirement / scope / node authoring 为 SSOT。面板只展示节点、前置、状态、进度和可触发 command，不新建科技树玩法系统，也不引入 `TechTreeStore`。

深度实现：`src/Libraries/Ludots.WebUI.PanelKit/`（`WebUiTechTree*`）。组合仍走 [WebUI Panel Kit Manifest](webui_panel_kit_manifest.md)；订阅仍走 [WebUI DataPlane](webui_dataplane_architecture.md)；玩法真相仍走 Progression runtime。

## 1. 概述

WPK-9 建立可复用的 TechTree / Progression 面板合同：

- Node source：`ProgressionDefinitionRegistry`、`ProgressionRequirementRegistry`、`ProgressionStateBuffer`、`ProgressionRequirementEvaluator`、`ScopeKey`。
- Layout / profile：树、网格、时代分层等由 profile / layout id 决定，不写死“科技/时代/传统/法令”语义。
- Status：`locked` / `available` / `active` / `completed` / `blocked`，由 state buffer + evaluator 投影。
- Actions：研究 / 解锁通过已注册 `WebUiCommandRouter` command（如 `progression.research`），浏览器不维护独立科技状态。
- Tooltip / 文案：display token 与 blocked reason token；缺 token fail-fast。

## 2. 结构

```text
Progression definitions + requirements + scope host
    -> ProgressionStateBuffer / ProgressionRequirementEvaluator
        -> WebUiTechTreeTopicProducer (DataPlane topic)
            -> WebUiTechTreeSnapshot (descriptor / profile / layout / scope / revision / nodes[])
                -> WPK-1 panel manifest.topic 订阅同一 DataPlane topic
        -> WebUiCommandRouter.Register(commandId, handler)
```

| 构件 | 职责 |
|------|------|
| `WebUiTechTreeNode` | nodeId、progressionId、requirementId、prerequisiteNodeIds、tokens、commandId、targetLevel、layoutSlot |
| `WebUiTechTreeDescriptor` | profile / layout / scopeKey + 校验后的节点图 |
| `WebUiTechTreeReferenceCatalog` | token / profile / layout / progression / requirement / scope / command 存在性钩子 |
| `WebUiTechTreeDescriptorLoader` | JSON 加载 + fail-fast 引用校验 |
| `WebUiTechTreeTopicProducer` | 从 Progression runtime 投影 snapshot |
| `WebUiTechTreePanelDescriptors` | 稳定 panelType / profile / layout / sample topic / research command |

## 3. 详情

### 3.1 复用

- Progression：`ProgressionDefinitionRegistry`、`ProgressionRequirementRegistry`、`ProgressionRequirementEvaluator`、`ProgressionStateBuffer`、`ScopeKey` / `ScopeKeyRegistry`、`ProgressionIdRegistry`、`ProgressionRequirementIdRegistry`
- WPK-1：`WebUiPanelKitManifest` / topic / profile / layout / `UiSurfaceHost`
- DataPlane：`IWebUiTopicProducer`、`WebUiOutboundPacket`、LatestWins、`WebUiDataPlaneRuntime`
- Command：`WebUiCommandRouter.Register(commandName, handler)`；节点只存 command 名引用
- Tooltip / rich text：display / blocked reason token 合同与 WPK-5 同口径（缺 token fail-fast）

### 3.2 新增

- PanelKit：`WebUiTechTreeContracts`、`WebUiTechTreeDescriptorLoader`、`WebUiTechTreeTopicProducer`、`WebUiTechTreeSampleCatalog`、`WebUiTechTreePanelDescriptors`
- Sample：`Samples/sample_techtree_descriptor.json`；sample manifest 增加 `hud.techtree`
- Sample topic：`panel-kit.sample.techtree`
- Sample research command：`progression.research`

### 3.3 Snapshot 形状

- `descriptor`、`profileId`、`layoutId`、`scopeKey`、`scopeHost`、`revision`
- `nodes[]`：`nodeId`、`progressionId`、`requirementId`、`prerequisiteNodeIds`、`status`、`level`、`targetLevel`、`blockedReasonTokenId`、`commandId`、`displayTokenId`、`layoutSlotId`、`sortOrder`

状态推导：

1. `level >= targetLevel` → `Completed`
2. `0 < level < targetLevel` → `Active`
3. 任一 prerequisite 节点未完成 → `Locked`
4. 有 requirement 且 evaluator 失败 → `Blocked`（必须带 blocked reason token）
5. 否则 → `Available`（可带已注册 commandId）

### 3.4 Fail-fast

- 未知 progression / requirement / scope / display token / blocked reason token / command
- requirement 存在但缺 blockedReasonTokenId
- prerequisite 指向未知 nodeId
- scope host 缺少 `ProgressionStateBuffer`
- progression 未进入 `ProgressionDefinitionRegistry`

禁止空串兜底、Unknown、静默跳过坏数据、新建 `TechTreeStore`、浏览器私有科技状态。

## 4. 场景

- 帝国时代：时代升级、兵种科技、经济科技 — 同一 descriptor，换 progression / requirement / layout。
- 星际：建筑科技、升级研究 — command 走正式 progression 入口。
- 群星：科技卡 / 传统树 / 飞升路径 — profile/layout 切换，不换 SSOT。
- CK3：文化革新、生活方式 perk — blocked reason token 解释前置。

## 5. 边界

- 不做新科技系统。所有可玩规则仍归 Progression / GAS / Requirement 管线。
- 不把具体游戏名、科技名写进可复用 PanelKit 代码。
- layout/profile 不写死“科技/时代/传统/法令”语义。
- 点击研究必须走已注册 WebUI command / progression 入口。

## 6. UAT

```gherkin
Feature: 科技树面板
  Scenario: 前置满足后节点变为可研究
    Given 一个科技节点依赖已完成的前置 Progression
    When Progression runtime 更新节点状态
    Then TechTree 面板显示该节点可研究
    And 点击节点通过正式 command/progression 入口提交
    And 浏览器没有维护独立科技状态

  Scenario: 缺引用时拒绝加载或投影
    Given TechTree descriptor 引用了未注册的 progression、requirement、scope、token 或 command
    When 加载 descriptor 或生产 DataPlane snapshot
    Then 操作失败
    And 错误信息包含缺失的具体 id
    And 玩家不会看到 Unknown 或空节点兜底
```

## 源码与测试

- 库：`src/Libraries/Ludots.WebUI.PanelKit/WebUiTechTree*.cs`
- Sample：`src/Libraries/Ludots.WebUI.PanelKit/Samples/sample_techtree_descriptor.json`
- 测试：`src/Tests/WebUiPanelKitTests/WebUiTechTreePanelTests.cs`
- PanelKit 回归：`src/Tests/WebUiPanelKitTests/WebUiPanelKitManifestTests.cs`
