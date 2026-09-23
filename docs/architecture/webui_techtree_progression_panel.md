# WebUI TechTree / Progression Panel（WPK-9）

TechTree 是 Progression node / requirement / scope / state 的展示视图，不是平行科技系统。本层在 WPK-1 Panel Kit manifest 之上提供 descriptor 与 DataPlane topic 合同，供科技树、传统树、飞升路径、文化革新等面板统一展示节点、前置、状态与可触发入口。

深度实现：`src/Libraries/Ludots.WebUI.PanelKit/`（`WebUiTechTree*`）。组合仍走 [WebUI Panel Kit Manifest](webui_panel_kit_manifest.md)；订阅仍走 [WebUI DataPlane](webui_dataplane_architecture.md)；富文本 tooltip 复用 WPK-5 token/localization 合同（`tooltipDescriptorId` 引用已注册 tooltip descriptor）。

## 1. 概述

WPK-9 建立 TechTree / Progression 面板的最小合同：

- Mod 作者用 JSON descriptor 声明节点、前置 progression、scope、requirement、layout 坐标、token 与正式 action 入口。
- 节点状态来自 `ProgressionDefinitionRegistry` + `ProgressionStateBuffer` + `ProgressionRequirementEvaluator`，浏览器不维护独立科技状态。
- DataPlane topic payload 固定包含 `scopeHost`、`actor`、`descriptor`、`profileId`、`layoutId`、`localeId`、`revision`、`nodes`。
- 节点 `status` / `action.actionKind` 的 JSON 线格式为 camelCase：`locked` / `available` / `active` / `completed`，以及 `command` / `ability` / `progression`（与 descriptor 作者字段一致；禁止 PascalCase `ToString()`）。
- layout / profile id 只描述树、网格、分层等几何，不写死“科技/时代/传统/法令”语义。
- 缺 node、progression、requirement、scope、token、action 时 fail-fast，错误含具体 id。

## 2. 结构

```text
TechTree descriptor JSON
    -> WebUiTechTreeDescriptorLoader（结构 + 引用校验）
        -> WebUiTechTreeDescriptor（nodes）
            -> WebUiTechTreeTopicProducer（IWebUiTopicProducer）
                -> payload: scopeHost / actor / descriptor / revision / nodes
            -> WPK-1 panel manifest.topic 引用同一 DataPlane topic
            -> 可选 tooltipDescriptorId 指向 WPK-5 rich tooltip
```

| 字段 | 含义 |
|------|------|
| `descriptorId` | 描述符稳定 id |
| `profileId` / `layoutId` / `localeId` | 展示 profile、布局、locale；必须已注册 |
| `nodeId` | 节点稳定 id；同一 descriptor 内唯一 |
| `progressionId` | Progression 定义 id；必须已注册 |
| `scopeKeyId` | Scope 键；必须已注册 |
| `unlockRequirementId` | 可选解锁 requirement；若声明则必须已注册 |
| `prerequisiteProgressionIds` | 前置 progression 列表；每项必须已注册 |
| `titleTokenId` / `bodyTokenId` / `effectTokenId` / `blockedReasonTokenId` | 文案 token |
| `groupId` / `sortOrder` / `layoutX` / `layoutY` | 分组与布局坐标 |
| `actionKind` / `actionId` | `command` / `ability` / `progression` + 已注册入口 id |
| `tooltipDescriptorId` | 可选 WPK-5 tooltip descriptor 引用 |

## 3. 详情

### 3.1 复用

- WPK-1：`WebUiPanelKitManifest` / topic / profile / layout / `UiSurfaceHost` 绑定。
- Progression：`ProgressionDefinitionRegistry`、`ProgressionIdRegistry`、`ProgressionRequirementIdRegistry`、`ProgressionStateBuffer`、`ProgressionRequirementEvaluator`、`ScopeKeyRegistry`。
- DataPlane：`IWebUiTopicProducer`、`WebUiDataPlaneRuntime.IsTopicRegistered`、`WebUiCommandRouter`（点击走已注册 command/ability/progression 入口）。
- Tooltip：节点可引用 WPK-5 `tooltipDescriptorId`；前置、效果、阻塞原因文案走 token/localization。

### 3.2 新增

- `WebUiTechTreeDescriptor` + loader/validator + sample catalog。
- `WebUiTechTreeTopicProducer` + snapshot payload 合同。
- Sample：`Samples/sample_techtree_descriptor.json`（通用 id，无游戏硬编码科技/时代/传统名）。

### 3.3 节点状态

| status | 条件 |
|--------|------|
| `completed` | scope host 的 `ProgressionStateBuffer` 已完成该 progression |
| `active` | 未完成，且可选 `isProgressionActive` 回调为真（研究中） |
| `available` | 未完成、前置与 unlock requirement 均满足 |
| `locked` | 前置或 requirement 未满足；payload 带 `blockedReasonTokenId` |

`available` / `active` 节点携带 `action`（`actionKind` + `actionId`），供浏览器提交到已注册正式入口。浏览器不得本地改写节点状态。

### 3.4 Fail-fast

未知 `progressionId` / 未注册 `ProgressionDefinition` / `unlockRequirementId` / `scopeKeyId` / display token / `actionId` / `profileId` / `layoutId` / `tooltipDescriptorId`（若提供注册表）/ 缺失 `ProgressionStateBuffer`，一律抛 `InvalidOperationException`，消息含具体 id。禁止空串、Unknown、默认锁定静默放过。

## 4. 场景

- 帝国时代：时代升级、兵种/经济节点 — profile/layout 换分层网格，progression id 来自 mod。
- 星际：建筑科技、升级研究 — 点击走 ability/command 入口。
- 群星：科技卡/传统树/飞升 — 同一 descriptor 形状，不同 layout profile。
- CK3：文化革新、生活方式 perk — requirement/scope 决定锁定原因。

## 5. 边界

- 不创建 `TechTreeStore` 或平行科技玩法系统。
- 不把“科技/时代/传统/法令”语义写进通用 PanelKit layout/profile 合同。
- 不在浏览器维护独立科技状态。
- 不新增平行 panel manifest / host / DataPlane。
- 不修改 GAS graph op / effect preset / entity lifecycle（本切片只读 Progression 投影并暴露已注册 action id）。

## 6. UAT

```gherkin
Feature: 科技树面板
  Scenario: 前置满足后节点变为可研究
    Given 一个科技节点依赖已完成的前置 Progression
    And TechTree descriptor、scope、requirement、token、action 都已注册
    When Progression runtime 更新节点状态
    Then TechTree 面板显示该节点可研究
    And 点击节点通过正式 command/progression 入口提交
    And 浏览器没有维护独立科技状态

  Scenario: 缺引用时失败
    Given descriptor 引用了未注册的 progression、requirement、scope 或 token
    When 加载 descriptor 或生产 topic snapshot
    Then 操作失败
    And 错误信息包含缺失的具体 id

  Scenario: 浏览器只订阅 manifest 声明的 topic
    Given WPK-1 panel manifest 的 techtree topic 指向 TechTree producer
    When 绑定 panel kit surface
    Then 浏览器订阅列表恰好等于 manifest 声明的 topic
```

## 源码与测试

- 库：`src/Libraries/Ludots.WebUI.PanelKit/WebUiTechTree*.cs`
- Sample：`src/Libraries/Ludots.WebUI.PanelKit/Samples/sample_techtree_descriptor.json`
- 测试：`src/Tests/WebUiPanelKitTests/WebUiTechTreePanelTests.cs`
